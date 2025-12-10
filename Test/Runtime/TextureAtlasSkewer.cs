using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;


namespace Splats.Test.Runtime {
    public class TextureAtlasSkewer : MonoBehaviour {
        #region ShaderCache
        static readonly int SOURCE_ATLAS = Shader.PropertyToID("SourceAtlas");
        static readonly int RESULT = Shader.PropertyToID("Result");
        static readonly int PAINT_DATA = Shader.PropertyToID("_Data");
        static readonly int OUTPUT_SIZE = Shader.PropertyToID("OutputSize");
        static readonly int REGION_OFFSET = Shader.PropertyToID("RegionOffset");
        
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
       
        [Header("Debug")]
        public bool showBoundingBox = true;
        public Color boundingBoxColor = Color.green;
        
        [Header("Output Settings")]
        public Vector2Int outputSize = new(512, 512);
        int kernelHandle;
        ComputeBuffer paintDataBuffer;
        Rect lastBoundingBox;
        
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


        /// <summary>
        /// Calculate the axis-aligned bounding box of the transformed sprite
        /// </summary>
        Rect CalculateBoundingBox(Vector2 spriteDimensions, Matrix2x2 transform, Vector2 centerPos) {
            // Define the 4 corners of the sprite in local space (centered at origin)
            Vector2 halfSize = spriteDimensions * 0.5f;
            Vector2[] corners = {
                new(-halfSize.x, -halfSize.y), // BL
                new( halfSize.x, -halfSize.y), // BR
                new(-halfSize.x,  halfSize.y), // TL
                new( halfSize.x,  halfSize.y)  // TR
            };

            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);

            // Apply transform then translate to position
            foreach (Vector2 corner in corners) {
                Vector2 transformed = transform * corner + centerPos;
                
                min.x = Mathf.Min(min.x, transformed.x);
                min.y = Mathf.Min(min.y, transformed.y);
                max.x = Mathf.Max(max.x, transformed.x);
                max.y = Mathf.Max(max.y, transformed.y);
            }

            // Clamp to output texture bounds
            min.x = Mathf.Max(0, min.x);
            min.y = Mathf.Max(0, min.y);
            max.x = Mathf.Min(outputSize.x, max.x);
            max.y = Mathf.Min(outputSize.y, max.y);

            return new Rect(
                min.x, 
                min.y, 
                Mathf.Max(0, max.x - min.x), 
                Mathf.Max(0, max.y - min.y)
            );
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

            // Calculate bounding box for the transformed sprite
            Rect boundingBox = CalculateBoundingBox(spriteDimensions, transformMatrix, position);
            lastBoundingBox = boundingBox; // Store for debug visualization

            // If bounding box is empty (sprite is completely off-screen), skip rendering
            if (boundingBox.width <= 0 || boundingBox.height <= 0) return;

            skewShader.SetTexture(kernelHandle, SOURCE_ATLAS, atlasTexture);
            skewShader.SetTexture(kernelHandle, RESULT, outputTexture);

            PaintData data = new(position, spriteDimensions, atlasRegion, transformMatrix.Inverse());
            PaintData[] tdBuff = { data };
            
            if (paintDataBuffer?.count < tdBuff.Length) {
                paintDataBuffer?.Release();
                paintDataBuffer = new ComputeBuffer(tdBuff.Length, Marshal.SizeOf<PaintData>());
            }

            paintDataBuffer.SetData(tdBuff);
            skewShader.SetBuffer(kernelHandle, PAINT_DATA, paintDataBuffer);
            
            // Set the region offset so shader knows where it's rendering
            skewShader.SetVector(REGION_OFFSET, new Vector2(boundingBox.x, boundingBox.y));
            skewShader.SetInts(OUTPUT_SIZE, outputSize.x, outputSize.y);

            // Dispatch only for the bounding box region
            int regionWidth = Mathf.CeilToInt(boundingBox.width);
            int regionHeight = Mathf.CeilToInt(boundingBox.height);
            int threadGroupsX = Mathf.CeilToInt(regionWidth / 8f);
            int threadGroupsY = Mathf.CeilToInt(regionHeight / 8f);
            
            skewShader.Dispatch(kernelHandle, threadGroupsX, threadGroupsY, 1);
        }
        
        void Update() {
            ExecuteSkew();
        }

        void OnDestroy() {
            if (outputTexture) outputTexture.Release();
            paintDataBuffer?.Release();
        }
        
        void OnGUI() {
            if (!showBoundingBox) return;
            
            float posX   = lastBoundingBox.x / outputSize.x * Screen.width;
            float posY   = Screen.height - (lastBoundingBox.y / outputSize.y * Screen.height);
            float widthX = lastBoundingBox.width / outputSize.x * Screen.width;
            float widthY = -(lastBoundingBox.height / outputSize.y * Screen.height);
            Rect  r      = new(posX, posY, widthX, widthY);
            
            // Draw bounding box outline
            GUI.backgroundColor = boundingBoxColor;
            GUI.Box(r, GUIContent.none);
        }
    }
}