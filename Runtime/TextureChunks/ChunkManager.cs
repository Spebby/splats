using System;
using Gilzoide.UpdateManager;
using UnityEngine;


namespace Gamba.Splats.TextureChunks {
    public class ChunkManager : IFixedUpdatable {
        readonly ChunkManagerSettings settings;
        Transform target;

        public Vector2Int[] Chunks;
        public Vector2Int CentreChunk => Chunks[Chunks.Length / 2];
        public int ChunkSize => settings.ChunkSize;
        public int Layers => settings.Layers;
        
        public Action OnChunkUpdate;
        
        public ChunkManager(Transform target, ChunkManagerSettings settings, bool centred = false) {
            this.target   = target;
            this.settings = settings;
            
            UpdateChunks();
            this.RegisterInManager();
        }

        ~ChunkManager() {
            this.UnregisterInManager();
        }
        
        public Vector2 ChunkToWorld(Vector2Int chunk) => chunk * ChunkSize;
        public Vector2 ChunkToWorldBL(Vector2Int chunk) => new(chunk.x * ChunkSize - Mathf.FloorToInt(ChunkSize * 0.5f), chunk.y * ChunkSize - Mathf.FloorToInt(ChunkSize * 0.5f));
        public void SetTarget(Transform target) => this.target = target;

        public void ManagedFixedUpdate() {
            if (InCentreChunk(target.position)) return;
            UpdateChunks();
        }

        #region Utility Functions
        void UpdateChunks() {
            Chunks = GetChunks(target.position, settings.Layers, settings.ChunkSize);
            OnChunkUpdate?.Invoke();
        }
        
        bool InCentreChunk(Vector2 position) {
            int pX = PosToChunkCoord(Mathf.RoundToInt(position.x), ChunkSize);
            int pY = PosToChunkCoord(Mathf.RoundToInt(position.y), ChunkSize);
            
            return pX == CentreChunk.x && pY == CentreChunk.y;
        }
        
        static Vector2Int[] GetChunks(Vector2 position, int layers, int chunkSize) {
            Vector2Int[] chunks = new Vector2Int[ChunkCount(layers)];

            int pX = PosToChunkCoord(Mathf.RoundToInt(position.x), chunkSize);
            int pY = PosToChunkCoord(Mathf.RoundToInt(position.y), chunkSize);

            int i = 0;
            for (int y = layers - 1; y > -layers; y--) {
                for (int x = -layers + 1; x < layers; x++) {
                    chunks[i++] = new Vector2Int(x + pX, y + pY);
                }
            }

            return chunks;
        }

        static int PosToChunkCoord(int x, int chunkSize) => Mathf.FloorToInt((x + chunkSize * 0.5f) / chunkSize);
        static int ChunkCount(int n) => (4 * n * n) - (4 * n) + 1;
        #endregion
    }
}
