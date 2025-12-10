using System.Runtime.InteropServices;
using UnityEngine;


namespace Splats.Test.Runtime {
    public class TextureAtlasSkewer : MonoBehaviour {
        #region ShaderCache
        static readonly int SOURCE_ATLAS = Shader.PropertyToID("SourceAtlas");
        static readonly int RESULT = Shader.PropertyToID("Result");
        static readonly int PAINT_DATA = Shader.PropertyToID("_Data");
        static readonly int OUTPUT_SIZE = Shader.PropertyToID("OutputSize");
        
        // GPU data holder
        readonly struct PaintData {
            // ReSharper disable NotAccessedField.Local
            readonly Vector2 position;
            readonly Vector2 spriteDimensions;
            readonly Vector4 atlasRegion;
            readonly Vector4 inverseTransformMatrix;
            // ReSharper restore NotAccessedField.Local

            public PaintData(Vector2 position, Vector2 spriteDimensions, Vector4 spriteAtlasRegion,
                             Vector4 inverseTransformMatrix) {
                this.position               = position;
                this.spriteDimensions       = spriteDimensions;
                atlasRegion                 = spriteAtlasRegion;
                this.inverseTransformMatrix = inverseTransformMatrix;
            }
        }
        #endregion
        
        [Header("Compute Shader")]
        public ComputeShader skewShader;

        [Header("Source Sprite")]
        public Sprite sprite;

        [Header("Output")]
        public RenderTexture outputTexture;

        [Header("Transform")]
        [Tooltip("Position in normalized coordinates (0-1), (0.5, 0.5) is center")]
        public Vector2 position = new(0.5f, 0.5f);
        public Matrix2x2 transformMatrix;
        public bool SetWithSliders;
        [Range(-Mathf.PI * 2, Mathf.PI * 2)] public float Rotation;
        public float Scale;
        public float SkewX;
        public float SkewY;
        
        [Header("Output Settings")]
        public Vector2Int outputSize = new(512, 512);
        int kernelHandle;
        ComputeBuffer paintDataBuffer;
        
        void Start() {
            Debug.Log(Marshal.SizeOf<PaintData>());
            InitializeRenderTexture();
            kernelHandle    = skewShader.FindKernel("CSMain");
            paintDataBuffer = new ComputeBuffer(1, Marshal.SizeOf<PaintData>());
        }

        void InitializeRenderTexture() {
            if (outputTexture) {
                outputSize = new Vector2Int(outputTexture.width, outputTexture.height);
                return;
            }

            outputTexture = new RenderTexture(outputSize.x, outputSize.y, 0,
                                              RenderTextureFormat.ARGB32) {
                enableRandomWrite = true
            };
            outputTexture.Create();
        }


        void ExecuteSkew() {
            if (!skewShader || !sprite) {
                Debug.LogError("Missing compute shader or sprite!");
                return;
            }

            InitializeRenderTexture();
            
            Texture2D atlasTexture = sprite.texture;
            Rect      spriteRect   = sprite.textureRect;

            Vector4 atlasRegion = new(
                x: spriteRect.x / atlasTexture.width,      // U offset
                y: spriteRect.y / atlasTexture.height,     // V offset
                z: spriteRect.width  / atlasTexture.width, // U size
                w: spriteRect.height / atlasTexture.height // V size
            );

            Vector2 spriteDimensions = new(spriteRect.width, spriteRect.height);

            if (SetWithSliders) {
                float s = Mathf.Sin(Rotation);
                float c = Mathf.Cos(Rotation);
                transformMatrix =  new Matrix2x2(Scale, 0, 0, Scale);
                transformMatrix *= new Matrix2x2(c, -s, s, c);
                transformMatrix *= new Matrix2x2(1, SkewX, SkewY, 1);
            }

            skewShader.SetTexture(kernelHandle, SOURCE_ATLAS, atlasTexture);
            skewShader.SetTexture(kernelHandle, RESULT, outputTexture);

            PaintData     Data      = new (position, spriteDimensions, atlasRegion, transformMatrix.Inverse());
            PaintData[]   tdBuff    = { Data };
            if (paintDataBuffer == null || paintDataBuffer.count < tdBuff.Length) {
                paintDataBuffer?.Release();
                paintDataBuffer = new ComputeBuffer(tdBuff.Length, Marshal.SizeOf<PaintData>());
            }
            paintDataBuffer.SetData(tdBuff);

            skewShader.SetBuffer(kernelHandle, PAINT_DATA, paintDataBuffer);
            skewShader.SetInts(OUTPUT_SIZE, outputSize.x, outputSize.y);

            // dispatch
            int threadGroupsX = Mathf.CeilToInt(outputSize.x / 8f);
            int threadGroupsY = Mathf.CeilToInt(outputSize.y / 8f);
            skewShader.Dispatch(kernelHandle, threadGroupsX, threadGroupsY, tdBuff.Length);
        }
        
        void Update() {
            ExecuteSkew();
        }

        void OnDestroy() {
            if (outputTexture) outputTexture.Release();
            paintDataBuffer?.Release();
        }
    }
}