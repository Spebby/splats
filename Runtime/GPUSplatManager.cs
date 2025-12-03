using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gilzoide.UpdateManager;
using Splats.TextureChunks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Color = UnityEngine.Color;


namespace Splats {
    public class GPUSplatManager : ISplatsManager, IUpdatable, ILateUpdatable {
        static readonly int SPLAT_MAP = Shader.PropertyToID("_SplatMap");
        
        
        ChunkManager cm;

        ISplatsConfig conf;
        RenderTexture[] sTexture;
        SplatChunk[] sChunks;

        CommandBuffer genCmb;

        RenderTexture cameraTexture;

        void ISplatsManager.Init(ISplatsConfig conf) {
            // TODO: fine for now but replace
            this.conf        =  conf;
            cm               =  new ChunkManager(Camera.main.transform, conf.cm_Settings);
            cm.OnChunkUpdate += SyncChunks;

            genCmb   = new CommandBuffer();
            sTexture = new RenderTexture[cm.Chunks.Length];
            for (int i = 0; i < sTexture.Length; i++) {
                sTexture[i] = new RenderTexture(conf.PixelsPerUnit * cm.ChunkSize,
                                                conf.PixelsPerUnit * cm.ChunkSize,
                                                0,
                                                RenderTextureFormat.RGHalf,
                                                RenderTextureReadWrite.Linear);
            }

            sChunks = new SplatChunk[cm.Chunks.Length];
            for (int i = 0; i < sChunks.Length; i++) {
                sChunks[i] = new SplatChunk(cm.Chunks[i],
                                            conf.PixelsPerUnit,
                                            cm.ChunkSize);
            }

            SyncChunks(); // fire it manually since we wouldn't have been able to subscribe before OnChunkUpdate fires for first time.
            this.RegisterInManager();
        }

        ~GPUSplatManager() {
            cm.OnChunkUpdate -= SyncChunks;
            cm               =  null;
            this.UnregisterInManager();
            sTexture = null;
            genCmb.Dispose();
        }


        void SyncChunks() {
            Vector2Int currCentre = sChunks[sChunks.Length / 2].chunkCoord;
            ShiftChunks(currCentre - cm.CentreChunk);
            Debug.Log($"New centre: {cm.CentreChunk}");
            
            // TODO: handle stitching for chunk boundaries.
            // Remove any splats that would get cutoff by the removal.
        }

        // Shift chunks by some offset
        // Limitation: this function assumes movement is continuous (no more than <+-1,+-1> movement) between chunks
        // For larger offsets, it really should just regenerate everything from scratch.
        void ShiftChunks(Vector2Int offset) {
            // It's important to remember that *offset* is the opposite of the player's movement direction.
            if (offset == Vector2Int.zero) return;
            int count = sChunks.Length;
            int layer = cm.Layers;
            int n     = layer + layer - 1;


            SplatChunk[]    temp = new SplatChunk[count];
            RenderTexture[] t2   = new RenderTexture[count];

            
            for (int y = layer - 1; y > -layer; y--) {
                for (int x = -layer + 1; x < layer; x++) {
                    int i        = XYToIndex(x, y, n);
                    int wrapX    = Wrap(x + offset.x, n);
                    int wrapY    = Wrap(y + offset.y, n);
                    int newIndex = XYToIndex(wrapX, wrapY, n);
                    
                    t2[newIndex] = sTexture[i];
                    
                    // In Bounds
                    if (!Remapped(x + offset.x, y + offset.y, n)) {
                        temp[newIndex] = sChunks[i];
                        continue;
                    }
                    
                    // Otherwise create new chunks where necessary & recycle the render textures
                    // We want the new chunk to be in the direction the player is heading,
                    // so we invert the offset to get the player's movement direction and add it.
                    temp[newIndex] = new SplatChunk(new Vector2Int(wrapX - offset.x, wrapY - offset.y), conf.PixelsPerUnit, cm.ChunkSize);
                    genCmb.SetRenderTarget(t2[i]);
                    genCmb.ClearRenderTarget(false, true, new Color(Random.value, Random.value, 0, 1.0f));
                }
            }

            sChunks  = temp;
            sTexture = t2;

            Graphics.ExecuteCommandBuffer(genCmb);
            genCmb.Clear();
            return;

            bool Remapped(int x, int y, int n) {
                int l = (n - 1) / 2;
                return (x < -l || l < x) || (y < -l || l < y);
            }

            int Wrap(int v, int n) {
                int l = (n - 1) / 2;
                return ((v + l) % n + n) % n - l;
            }
        }
       
        // -(2ny - n^2 -2x + 1) / 2
        static int XYToIndex(int x, int y, int n) => -(2 * n * y - n * n - 2 * x + 1) / 2;
        
        public void Spawn(Vector2 position, Quaternion rotation, SplatParams @params) {
            throw new System.NotImplementedException();
        }

        public SplatHit Query(Vector2 position, float radius) {
            throw new System.NotImplementedException();
        }

        public NativeArray<SplatHit> Query(NativeArray<Vector2> positions, NativeArray<float> radii,
                                           Allocator allocator = Allocator.Temp) {
            throw new System.NotImplementedException();
        }




        public void ManagedUpdate() { }

        public void ManagedLateUpdate() {
            StitchChunks(Camera.main);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void EnsureCameraTexture(Camera cam) {
            if (!cameraTexture || cameraTexture.width != cam.pixelWidth || cameraTexture.height != cam.pixelHeight) {
                if (cameraTexture) cameraTexture.Release();
                cameraTexture = new RenderTexture(cam.pixelWidth,
                                                  cam.pixelHeight,
                                                  0,
                                                  RenderTextureFormat.RGHalf,
                                                  RenderTextureReadWrite.Linear) {
                    enableRandomWrite = false
                };
                cameraTexture.Create();
            }

            genCmb.SetRenderTarget(cameraTexture);
            genCmb.ClearRenderTarget(false, true, Color.clear);
        }

        void StitchChunks(Camera cam) {
            EnsureCameraTexture(cam);

            int     ppu       = conf.PixelsPerUnit;
            int     chunkSize = cm.ChunkSize;
            Vector2 camPos    = cam.transform.position;
            float   halfW     = cam.orthographicSize * cam.aspect;
            float   halfH     = cam.orthographicSize;

            Vector2 camBL = new(camPos.x - halfW, camPos.y - halfH);
            Vector2 camTR = new(camPos.x + halfW, camPos.y + halfH);

            CommandBuffer cmb = new();
            cmb.name = "StitchOp";
            cmb.SetRenderTarget(cameraTexture);
            cmb.ClearRenderTarget(true, true, Color.clear);
            
            // This works correctly but is prone to appearing wrong if Camera PPU != RenderTexture PPU.
            // TODO: resize to match Camera PPU.
            for (int i = 0; i < sChunks.Length; i++) {
                // todo: fix that this not being offset
                Vector2 chunkBL = cm.ChunkToWorldBL(sChunks[i].chunkCoord);
                if (!ChunkIntersectsCam(camBL, camTR, chunkBL, sChunks[i].chunkSize, out Vector2 overlapBL, out Vector2 overlapTR)) continue;

                // Chunk is in bounds... calculate which part of the texture will actually be rendered.
                int     camPpu  = Mathf.FloorToInt(cam.pixelHeight / (2f * cam.orthographicSize));
                RectInt srcRect = WorldToDstPixels(overlapBL, overlapTR, chunkBL, ppu);
                RectInt dstRect = WorldToDstPixels(overlapBL, overlapTR, camBL, camPpu);

                
                srcRect = ClampRect(srcRect, sTexture[i].width, sTexture[i].height);
                dstRect = ClampRect(dstRect, cameraTexture.width, cameraTexture.height);

                if (srcRect.width == 0 || dstRect.width == 0)
                    continue;

                // copyTexture over blit, b/c we just want to copy pixels without any filtering/scaling
                cmb.CopyTexture(
                    sTexture[i], 0, 0, // source texture, mip 0, element 0
                    srcRect.x, srcRect.y,
                    srcRect.width, srcRect.height,
                    cameraTexture, 0, 0, // dest texture, mip 0, element 0
                    dstRect.x, dstRect.y
                );
            }

            Graphics.ExecuteCommandBuffer(cmb);
            cmb.Release();
            
            Shader.SetGlobalTexture(SPLAT_MAP, cameraTexture);
        }

        // Is the chunk in question inside camera bounds?
        static bool ChunkIntersectsCam(Vector2 camBL, Vector2 camTR, Vector2 chunkBL, float chunkSize,
                                         out Vector2 overlapBL, out Vector2 overlapTR) {
            Vector2 chunkTR = chunkBL + new Vector2(chunkSize, chunkSize);
            overlapBL = new Vector2(
                Mathf.Max(camBL.x, chunkBL.x),
                Mathf.Max(camBL.y, chunkBL.y)
            );

            overlapTR = new Vector2(
                Mathf.Min(camTR.x, chunkTR.x),
                Mathf.Min(camTR.y, chunkTR.y)
            );

            return !(overlapTR.x <= overlapBL.x) && !(overlapTR.y <= overlapBL.y);
        }


        // Convert overlap region → pixel rect inside dst render texture
        static RectInt WorldToDstPixels(Vector2 oBL, Vector2 oTR, Vector2 dstBL, int ppu) {
            float x = (oBL.x - dstBL.x) * ppu;
            float y = (oBL.y - dstBL.y) * ppu;

            float width  = (oTR.x - oBL.x) * ppu;
            float height = (oTR.y - oBL.y) * ppu;

            return new RectInt(
                Mathf.RoundToInt(x),
                Mathf.RoundToInt(y),
                Mathf.RoundToInt(width),
                Mathf.RoundToInt(height));
        }
        
        static RectInt ClampRect(RectInt r, int texWidth, int texHeight) {
            int xMin = Mathf.Clamp(r.xMin, 0, texWidth);
            int yMin = Mathf.Clamp(r.yMin, 0, texHeight);
            int xMax = Mathf.Clamp(r.xMax, 0, texWidth);
            int yMax = Mathf.Clamp(r.yMax, 0, texHeight);

            return xMax <= xMin || yMax <= yMin
                ? new RectInt(0, 0, 0, 0)
                : new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }
    }
    

    [StructLayout(LayoutKind.Sequential)]
    readonly struct SplatChunk {
        public readonly Vector2Int chunkCoord; // chunk grid coords
        public readonly int ppu;  // texture size in pixels
        public readonly int chunkSize;      // size in world units

        public SplatChunk(Vector2Int chunkCoord, int ppu, int chunkSize) {
            this.chunkCoord = chunkCoord;
            this.ppu        = ppu;
            this.chunkSize  = chunkSize;
        }
    }
}
