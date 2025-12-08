using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gilzoide.UpdateManager;
using Splats.TextureChunks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Color = UnityEngine.Color;


namespace Splats {
    public class GPUSplatManager : ISplatsManager, IUpdatable, ILateUpdatable {
        ComputeShader lifetimeCompute;
        ComputeShader queryCompute;
        static readonly int SPLAT_MAP = Shader.PropertyToID("_SplatMap");
        static readonly int OUT_VALUE = Shader.PropertyToID("_OutValue");
        static readonly int CHUNKS = Shader.PropertyToID("_Chunks");
        static readonly int CHUNK_SLICE_MAP = Shader.PropertyToID("_ChunkSliceMap");
        static readonly int QUERY_PARAMS = Shader.PropertyToID("_QueryParams");

        ChunkManager cm;
        ISplatsConfig conf;
        SplatChunk[] sChunks;
        Texture2DArray sTexture;
        int[] sliceMap;

        CommandBuffer genCmb;
        RenderTexture cameraTexture;

        void ISplatsManager.Init(ISplatsConfig c) {
            // TODO: fine for now but replace
            conf             =  c;
            cm               =  new ChunkManager(Camera.main!.transform, conf.cm_Settings);
            cm.OnChunkUpdate += SyncChunks;

            genCmb  = new CommandBuffer();
            sChunks = new SplatChunk[cm.Chunks.Length];
            for (int i = 0; i < sChunks.Length; i++) {
                sChunks[i] = new SplatChunk(cm.Chunks[i],
                                            c.PixelsPerUnit,
                                            cm.ChunkSize);
            }

            sTexture = new Texture2DArray(conf.PixelsPerUnit * cm.ChunkSize,
                                          conf.PixelsPerUnit * cm.ChunkSize,
                                          cm.Chunks.Length,
                                          GraphicsFormat.R16G16_SFloat,
                                          TextureCreationFlags.None,
                                          0);
            
            sliceMap = new int[cm.ChunkSize];
            for (int i = 0; i < sliceMap.Length; i++) sliceMap[i] = i;

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
            
            Shader.SetGlobalTexture(CHUNKS, sTexture);
            // Consider buffering edge & use dither/gradient to remove pixels along edge, avoid straight edge
            // maybe chunks overlap each other slightly
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


            SplatChunk[] temp = new SplatChunk[count];
            int[] t2          = new int[count];

            
            for (int y = layer - 1; y > -layer; y--) {
                for (int x = -layer + 1; x < layer; x++) {
                    int wrapX    = Wrap(x + offset.x, n);
                    int wrapY    = Wrap(y + offset.y, n);
                    int i        = ChunkManager.XYToIndex(x, y, n);
                    int newIndex = ChunkManager.XYToIndex(wrapX, wrapY, n);
                    
                    t2[newIndex] = sliceMap[i];
                    
                    // In Bounds
                    if (!Remapped(x + offset.x, y + offset.y, n)) {
                        temp[newIndex] = sChunks[i];
                        continue;
                    }
                    
                    // Otherwise create new chunks where necessary & recycle the render textures
                    // We want the new chunk to be in the direction the player is heading,
                    // so we invert the offset to get the player's movement direction and add it.
                    temp[newIndex] = new SplatChunk(new Vector2Int(wrapX - offset.x, wrapY - offset.y), conf.PixelsPerUnit, cm.ChunkSize);

                    RenderTargetIdentifier rti = new(sTexture, 0, CubemapFace.Unknown, t2[newIndex]);
                    genCmb.SetRenderTarget(rti);
                    genCmb.ClearRenderTarget(false, true, new Color(Random.value, Random.value, 0, 1.0f));
                }
            }

            sChunks  = temp;
            sliceMap = t2;

            Graphics.ExecuteCommandBuffer(genCmb);
            genCmb.Clear();
            
            ComputeBuffer tempBuff = new(count: sliceMap.Length, stride: sizeof(int));
            ComputeHelper.CreateStructuredBuffer(ref tempBuff, sliceMap);
            Shader.SetGlobalBuffer(CHUNK_SLICE_MAP, tempBuff);
            return;

            bool Remapped(int x, int y, int len) {
                int l = (len - 1) / 2;
                return (x < -l || l < x) || (y < -l || l < y);
            }

            int Wrap(int v, int len) {
                int l = (len - 1) / 2;
                return ((v + l) % len + len) % len - l;
            }
        }
        
        public void Spawn(Vector2 position, Quaternion rotation, SplatParams @params) {
            
            // first, assemble the splat texture.
            // For now, just select a random one
            ISplatSettings conf = SplatsRegistry.Get(@params.ID);
            
            
            Shader.SetGlobalTexture(CHUNKS, sTexture);
            // sending to gpu 4x is probably best for chunk boundaries
        }

        const int KERNEL_QUERY = 0;
       
        // Data holder for marshalling data to GPU
        [SuppressMessage("ReSharper", "NotAccessedField.Local")]
        readonly struct SQueryParams {
            public readonly int ChunkSlice;
            public readonly int MinPx, MinPy, MaxPx, MaxPy;
            public readonly Vector2 CircleCenter;
            public readonly float CircleRadius;

            public SQueryParams(int chunkSlice, int minPx, int minPy, int maxPx, int maxPy, Vector2 circleCenter, float radius = 1.0f) {
                ChunkSlice = chunkSlice;
                MinPx = minPx;
                MinPy = minPy;
                MaxPx = maxPx;
                MaxPy = maxPy;
                CircleCenter = circleCenter;
                CircleRadius = radius;
            }
        }

        public SplatHit Query(Vector2 position, float radius) {
            NativeArray<Vector2> posArr = new(1, Allocator.Temp);
            NativeArray<float>   radArr = new(1, Allocator.Temp);

            posArr[0] = position;
            radArr[0] = radius;

            NativeArray<SplatHit> result = Query(posArr, radArr);
            posArr.Dispose();
            radArr.Dispose();

            return result[0];
        }
        
        // I have not tested this yet but Radii should be capped to 0.5 since we can only handle 8x8 atm.
        // def refactor this at some point but this is acceptable for the sketch stage
        public NativeArray<SplatHit> Query(NativeArray<Vector2> positions,
                                   NativeArray<float> radii,
                                   Allocator allocator = Allocator.Temp) {
            int                       count = positions.Length;
            NativeArray<SQueryParams> qps   = new(count, allocator);

            // Build CPU-side struct array
            for (int i = 0; i < count; i++) {
                int     chunkIndex  = cm.PosToChunkIndex(positions[i]);
                Vector2 chunkOrigin = cm.ChunkToWorldBL(positions[i]);
                int     texSize     = cm.ChunkSize * conf.PixelsPerUnit;
                qps[i] = CreateSQP(positions[i], radii[i], chunkOrigin, texSize,
                                   chunkIndex, cm.ChunkSize, conf.PixelsPerUnit);
            }

            // Upload to GPU
            int           stride = Marshal.SizeOf<SQueryParams>();
            ComputeBuffer qpBuf  = new(count, stride);
            ComputeBuffer outBuf = new(count, sizeof(uint));
            qpBuf.SetData(qps);

            queryCompute.SetBuffer(KERNEL_QUERY, QUERY_PARAMS, qpBuf);
            queryCompute.SetBuffer(KERNEL_QUERY, OUT_VALUE, outBuf);
            // 1 group per query
            queryCompute.Dispatch(KERNEL_QUERY, count, 1, 1);
            // Read results
            uint[] raw = new uint[count];
            outBuf.GetData(raw);

            qpBuf.Release();
            outBuf.Release();

            // Wrap results
            NativeArray<SplatHit> hits              = new(count, allocator);
            for (int i = 0; i < count; i++) hits[i] = new SplatHit(raw[i], -1);
            return hits;
            
            
            // ReSharper disable once InconsistentNaming
            SQueryParams CreateSQP(Vector2 position, float radius, Vector2 chunkOrigin, int texSize,
                                   int chunkIndex, int chunkSize, int ppu) {
                Vector2 minW      = position - new Vector2(radius, radius);
                Vector2 maxW      = position + new Vector2(radius, radius);
                Vector2 chunkMax  = chunkOrigin + new Vector2(chunkSize, chunkSize);
                Vector2 clampMinW = Vector2.Max(minW, chunkOrigin);
                Vector2 clampMaxW = Vector2.Min(maxW, chunkMax);

                // convert to pixel coords
                Vector2 minPxF = (clampMinW - chunkOrigin) * ppu;
                Vector2 maxPxF = (clampMaxW - chunkOrigin) * ppu;

                int minPx = Mathf.FloorToInt(minPxF.x);
                int minPy = Mathf.FloorToInt(minPxF.y);
                int maxPx = Mathf.CeilToInt (maxPxF.x);
                int maxPy = Mathf.CeilToInt (maxPxF.y);
                minPx = Mathf.Clamp(minPx, 0, texSize - 1);
                minPy = Mathf.Clamp(minPy, 0, texSize - 1);
                maxPx = Mathf.Clamp(maxPx, 1, texSize);
                maxPy = Mathf.Clamp(maxPy, 1, texSize);
                SQueryParams sqp = new(sliceMap[chunkIndex], minPx, minPy, maxPx, maxPy, position, radius);
                return sqp;
            }
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
                Vector2 chunkTL = new(chunkBL.x + chunkSize, chunkBL.y + chunkSize);
                if (!RectOverlap(camBL, camTR, chunkBL, chunkTL, out Vector2 overlapBL, out Vector2 overlapTR)) continue;

                // Chunk is in bounds... calculate which part of the texture will actually be rendered.
                int     camPpu  = Mathf.FloorToInt(cam.pixelHeight / (2f * cam.orthographicSize));
                RectInt srcRect = WorldToDstPixels(overlapBL, overlapTR, chunkBL, ppu);
                RectInt dstRect = WorldToDstPixels(overlapBL, overlapTR, camBL, camPpu);

                int                    w   = cm.ChunkSize * conf.PixelsPerUnit;
                srcRect = ClampRect(srcRect, w, w);
                dstRect = ClampRect(dstRect, cameraTexture.width, cameraTexture.height);

                if (srcRect.width == 0 || dstRect.width == 0)
                    continue;

                // copyTexture over blit, b/c we just want to copy pixels without any filtering/scaling
                RenderTargetIdentifier rti = new(sTexture, 0, CubemapFace.Unknown, sliceMap[i]);
                cmb.CopyTexture(
                    rti, 0, 0, // source texture, mip 0, element 0
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
        static bool RectOverlap(Vector2 dstBL, Vector2 dstTR, Vector2 brushBL, Vector2 brushTR,
                                         out Vector2 overlapBL, out Vector2 overlapTR) {
            overlapBL = new Vector2(
                Mathf.Max(dstBL.x, brushBL.x),
                Mathf.Max(dstBL.y, brushBL.y)
            );

            overlapTR = new Vector2(
                Mathf.Min(dstTR.x, brushTR.x),
                Mathf.Min(dstTR.y, brushTR.y)
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
