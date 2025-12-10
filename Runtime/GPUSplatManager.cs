using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Gilzoide.UpdateManager;
using Splats.TextureChunks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Color = UnityEngine.Color;
using Random = UnityEngine.Random;


namespace Splats {
    public class GPUSplatManager : ISplatsManager, IUpdatable, ILateUpdatable {
        ComputeShader occupancyCompute;
        ComputeShader queryCompute;
        static readonly int SPLAT_MAP = Shader.PropertyToID("_SplatMap");
        static readonly int OUT_VALUE = Shader.PropertyToID("_OutValue");
        static readonly int CHUNKS = Shader.PropertyToID("_Chunks");
        static readonly int CHUNK_SLICE_MAP = Shader.PropertyToID("_ChunkSliceMap");
        static readonly int QUERY_PARAMS = Shader.PropertyToID("_QueryParams");
        static readonly int SPLAT_TEX = Shader.PropertyToID("_SplatTex");
        static readonly int SPLAT_SPAWNS = Shader.PropertyToID("_splatSpawns");

        ChunkManager cm;
        ISplatsConfig conf;
        SplatChunk[] sChunks;
        Texture2DArray sTexture;
        int[] sliceMap;

        CommandBuffer genCmb;
        RenderTexture cameraTexture;
        
        // like a semaphore gate
        int readingGPU;

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
            Vector2Int currCentre    = sChunks[sChunks.Length / 2].chunkCoord;
            Vector2Int offset = currCentre - cm.CentreChunk;
            if (offset == Vector2Int.zero) return;

            _ = SyncChunksAsync(offset);
        }

        async Task SyncChunksAsync(Vector2Int offset) {
            // Wait for all GPU readbacks to finish
            while (readingGPU > 0) {
                await Task.Yield(); // yield to Unity's main thread
            }

            ShiftChunks(offset);
            Debug.Log($"New centre: {cm.CentreChunk}");
            
            // Todo: handle stitching for chunk boundaries.
            // We only need to worry about stitching for chunks that are either in view, or next to newly generated chunks
            // No point in wasting compute stitching what'll likely be culled next shift.
            // Remove any splats that would get cutoff by the removal.
            
            Shader.SetGlobalTexture(CHUNKS, sTexture);
            // Consider buffering edge & use dither/gradient to remove pixels along edge, avoid straight edge
            // maybe chunks overlap each other slightly
        }

        
        // Shift chunks by some offset
        // Limitation: this function assumes movement is continuous (no more than <+-1,+-1> movement) between chunks
        // For larger offsets, it really should just regenerate everything from scratch.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ShiftChunks(Vector2Int offset) {
            // It's important to remember that *offset* is the opposite of the player's movement direction.
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

        [StructLayout(LayoutKind.Sequential)]
        readonly struct SplatSpawnData {
            public readonly int ChunkIndex;
            public readonly int startX;
            public readonly int startY;
            public readonly int endX;
            public readonly int endY;
            public readonly Vector4 UVRect;
            public readonly Vector4 Matrix;
            public readonly uint ID;

            public SplatSpawnData(uint id, Vector2 origin, Vector2 extents, Matrix2x2 matrix, Vector4 uvRect, int chunkIndex) {
                startX     = Mathf.FloorToInt(origin.x);
                startY     = Mathf.FloorToInt(origin.y);
                endX       = Mathf.CeilToInt(origin.x + extents.x);
                endY       = Mathf.CeilToInt(origin.y + extents.y);
                UVRect     = uvRect;
                Matrix     = new Vector4(matrix.m00, matrix.m01, matrix.m10, matrix.m11);
                ChunkIndex = chunkIndex;
                ID         = id;
            }
        }
        
        const int KERNEL_SPAWN = 0;
        const int KERNEL_LIFETIME = 1;
        public void Spawn(Vector2 position, SplatParams @params) {
            // first, assemble the splat texture.
            // I'd like to have done this now, but time is tight, and I'd rather get a basic version working.
            // For now, side stepping by just getting a single, random texture.
            
            // For now, just select a random one
            ISplatSettings splatSettings = SplatsRegistry.Get(@params.ID);
            Sprite splatSprite = splatSettings.RandomTexture;
            
            // we can reasonably assume sprites are packed, so we should account for that
            Texture2D texture = splatSprite.texture;
            Rect spriteUV = splatSprite.textureRect;
            Vector4 uvRect = new(
                spriteUV.xMin / splatSprite.texture.width,
                spriteUV.yMin / splatSprite.texture.height,
                spriteUV.width / splatSprite.texture.width,
                spriteUV.height / splatSprite.texture.height
            );
            
            float r = ComputeWorldRadius(@params.Transformation, splatSprite.bounds.extents.x);
            Vector2 splatTR = new( r + position.x,  r + position.y);
            Vector2 splatTL = new(-r + position.x,  r + position.y);
            Vector2 splatBR = new( r + position.x, -r + position.y);
            Vector2 splatBL = new(-r + position.x, -r + position.y);
            
            int       count = 0;
            Span<int> arr   = stackalloc int[4];
            AddUnique(arr, cm.PosToChunkIndex(splatTR));
            AddUnique(arr, cm.PosToChunkIndex(splatTL));
            AddUnique(arr, cm.PosToChunkIndex(splatBR));
            AddUnique(arr, cm.PosToChunkIndex(splatBL));

            // calculate rects & dispatch
            ComputeBuffer ssdbuff = new(count, Marshal.SizeOf<SplatSpawnData>());
            NativeArray<SplatSpawnData> ssdbuffTemp = new(count, Allocator.Temp);
            for (int i = 0; i < count; i++) {
                Vector2 chunkBL = cm.ChunkToWorldBL(sChunks[arr[i]].chunkCoord);
                Vector2 chunkTL = new(chunkBL.x + cm.ChunkSize, chunkBL.y + cm.ChunkSize);
                RectOverlap(splatBL, splatTR, chunkBL, chunkTL, out Vector2 overlapBL, out Vector2 overlapTR);
                RectInt srcRect = WorldToDstPixels(overlapBL, overlapTR, chunkBL, conf.PixelsPerUnit);
                int     w       = cm.ChunkSize * conf.PixelsPerUnit;
                ClampRect(srcRect, w, w);
                if (srcRect.width == 0) continue;
                
                // dispatch or prep to be batch dispatch
                int sliceIndex = sliceMap[arr[i]];
                ssdbuffTemp[i] = new SplatSpawnData(@params.ID, srcRect.min, srcRect.max, @params.Transformation, uvRect, sliceIndex);
            }
            ssdbuff.SetData(ssdbuffTemp);
            ssdbuffTemp.Dispose();
            
            // send dis shit to GPU for painting
            occupancyCompute.SetBuffer(KERNEL_SPAWN, SPLAT_SPAWNS, ssdbuff);
            occupancyCompute.SetTexture(KERNEL_SPAWN, SPLAT_TEX, texture);
            
            //occupancyCompute.Dispatch(KERNEL_SPAWN, count, 1, 1); 
            
            
            // read updates to render textures from GPU (only ones we've updated)
            // do this async
            readingGPU++;
            AsyncGPUReadback.Request(sTexture, 0, TextureFormat.RFloat, OnCompleteSpawnReadback);
            
            // we do not need to re-update the occupancy map since it's already up to date.
            return;

            void AddUnique(Span<int> arr, int value) {
                for (int i = 0; i < count; i++) {
                    if (arr[i] == value) return;
                }

                arr[count++] = value;
            }
        }

        void OnCompleteSpawnReadback(AsyncGPUReadbackRequest request) {
            // readback data
            
            readingGPU--;
        }

        // Used to calculate a mock worst case bounding box for a transformed splat.
        static float ComputeWorldRadius(Matrix2x2 m, float width) {
            // sprite local half-size in world units
            Vector2[] corners = {
                new(-width, -width),
                new(-width,  width),
                new( width, -width),
                new( width,  width)
            };

            float max = 0f;
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (Vector2 c in corners) {
                Vector2 t = m * c;
                float   r = t.magnitude;
                if (r > max)
                    max = r;
            }

            return max;
        }
        
        const int KERNEL_QUERY = 0;
       
        // Data holder for marshalling data to GPU
        [StructLayout(LayoutKind.Sequential)]
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
            // TODO: this currently doesn't account for PPU properly
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

                int w   = cm.ChunkSize * conf.PixelsPerUnit;
                srcRect = ClampRect(srcRect, w, w);
                dstRect = ClampRect(dstRect, cameraTexture.width, cameraTexture.height);

                if (srcRect.width == 0 || dstRect.width == 0) continue;

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
