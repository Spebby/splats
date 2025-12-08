using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using ThreadGroup = System.Tuple<uint, uint, uint>;


namespace Splats {
    // Mostly lifted from Sebastian Lague's helper w/ minor adjustments & some omissions
    // https://github.com/SebLague/Slime-Simulation/blob/main/Assets/Scripts/Compute%20Helper/ComputeHelper.cs
    public static class ComputeHelper {
        /// Convenience method for dispatching a compute shader.
        /// Calculates the number of thread groups based on the number of iterations needed.
        public static void Dispatch(ComputeShader cs, int numIterationsX, int numIterationsY = 1, int numIterationsZ = 1, int kernelIndex = 0) {
            ThreadGroup threadGroupSizes = GetThreadGroupSizes(cs, kernelIndex);
            int         numGroupsX       = Mathf.CeilToInt(numIterationsX / (float)threadGroupSizes.Item1);
            int         numGroupsY       = Mathf.CeilToInt(numIterationsY / (float)threadGroupSizes.Item2);
            int         numGroupsZ       = Mathf.CeilToInt(numIterationsZ / (float)threadGroupSizes.Item3);
            cs.Dispatch(kernelIndex, numGroupsX, numGroupsY, numGroupsZ);
        }

        public static void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, int count) {
            int stride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
            bool createNewBuffer = buffer == null || !buffer.IsValid() || buffer.count != count || buffer.stride != stride;
            if (!createNewBuffer) return;
            Release(buffer);
            buffer = new ComputeBuffer(count, stride);
        }

        public static void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, T[] data) {
            CreateStructuredBuffer<T>(ref buffer, data.Length);
            buffer.SetData(data);
        }
        
        public static ComputeBuffer CreateAndSetBuffer<T>(T[] data, ComputeShader cs, string nameID,
                                                          int kernelIndex = 0) {
            ComputeBuffer buffer = null;
            CreateAndSetBuffer(ref buffer, data, cs, nameID, kernelIndex);
            return buffer;
        }

        public static void CreateAndSetBuffer<T>(ref ComputeBuffer buffer, T[] data, ComputeShader cs, string nameID,
                                                 int kernelIndex = 0) {
            CreateStructuredBuffer<T>(ref buffer, data.Length);
            buffer.SetData(data);
            cs.SetBuffer(kernelIndex, nameID, buffer);
        }

        public static ComputeBuffer CreateAndSetBuffer<T>(int length, ComputeShader cs, string nameID,
                                                          int kernelIndex = 0) {
            ComputeBuffer buffer = null;
            CreateAndSetBuffer<T>(ref buffer, length, cs, nameID, kernelIndex);
            return buffer;
        }

        public static void CreateAndSetBuffer<T>(ref ComputeBuffer buffer, int length, ComputeShader cs, string nameID,
                                                 int kernelIndex = 0) {
            CreateStructuredBuffer<T>(ref buffer, length);
            cs.SetBuffer(kernelIndex, nameID, buffer);
        }
        
        public static void Release(params ComputeBuffer[] buffers) {
            foreach (ComputeBuffer t in buffers) {
                t?.Release();
            }
        }
        
        public static ThreadGroup GetThreadGroupSizes(ComputeShader compute, int kernelIndex = 0) {
            compute.GetKernelThreadGroupSizes(kernelIndex, out uint x, out uint y, out uint z);
            return new ThreadGroup(x, y, z);
        }
        
        public static void CreateRenderTexture(ref RenderTexture texture, int width, int height, FilterMode filterMode = FilterMode.Point, GraphicsFormat format = GraphicsFormat.R16G16B16A16_SFloat) {
            if (texture && texture.IsCreated() && texture.width == width && texture.height == height &&
                texture.graphicsFormat == format) return;
            if (texture) texture.Release();
                
            texture               = new RenderTexture(width, height, 0) {
                graphicsFormat    = format,
                enableRandomWrite = true,
                autoGenerateMips  = false,
                wrapMode          = TextureWrapMode.Clamp,
                filterMode        = filterMode,
            };

            texture.Create();
        }
        
        // https://cmwdexint.com/2017/12/04/computeshader-setfloats/
        public static float[] PackFloats(params float[] values) {
            float[] packed = new float[values.Length * 4];
            if (packed == null) throw new ArgumentNullException(nameof(packed));
            for (int i = 0; i < values.Length; i++) {
                packed[i * 4] = values[i];
            }
            return values;
        }
    }
}