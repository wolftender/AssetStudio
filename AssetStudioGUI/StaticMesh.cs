using AssetStudio;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using System;
using System.Diagnostics;
using Vector2 = OpenTK.Mathematics.Vector2;
using Vector3 = OpenTK.Mathematics.Vector3;
using Vector4 = OpenTK.Mathematics.Vector4;

namespace AssetStudioGUI
{
	internal class StaticMesh : IDisposable
	{
		// buffers
		private int m_vbPosition, m_vbUv, m_vbNormal, m_vbColor, m_vbIndex;
		private int m_vao;

		// vertices num
		private int m_numIndices;

		public StaticMesh(Vector3[] positions, Vector4[] colors, Vector3[] normals, int[] indices) {
			m_vao = GL.GenVertexArray();
			GL.BindVertexArray(m_vao);

			Renderer.CreateVBO(out m_vbPosition, positions, Renderer.SHADER_ATTRIB_POSITION);
			Renderer.CreateVBO(out m_vbNormal, normals, Renderer.SHADER_ATTRIB_NORMAL);
			Renderer.CreateVBO(out m_vbColor, colors, Renderer.SHADER_ATTRIB_COLOR);
			Renderer.CreateEBO(out m_vbIndex, indices);

			GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
			GL.BindVertexArray(0);

			m_numIndices = indices.Length;
		}

		public void Dispose()
		{
			if (m_vao != 0)
			{
				GL.DeleteVertexArray(m_vao);
			}

			if (m_vbPosition != 0)
			{
				GL.DeleteBuffer(m_vbPosition);
			}

			if (m_vbNormal != 0)
			{
				GL.DeleteBuffer(m_vbNormal);
			}

			if (m_vbColor != 0)
			{
				GL.DeleteBuffer(m_vbColor);
			}

			if (m_vbIndex != 0)
			{
				GL.DeleteBuffer(m_vbIndex);
			}

			if (m_vbUv != 0)
			{
				GL.DeleteBuffer(m_vbUv);
			}

			m_vbPosition = 0;
			m_vbUv = 0;
			m_vbNormal = 0;
			m_vbColor = 0;
			m_vbIndex = 0;
		}

		public void Bind()
		{
			GL.BindVertexArray(m_vao);
		}

		public void Draw()
		{
			GL.DrawElements(BeginMode.Triangles, m_numIndices, DrawElementsType.UnsignedInt, 0);
		}

		public static StaticMesh FromMesh(Mesh mesh, out Matrix4 worldMatrix)
		{
			if (mesh.m_VertexCount <= 0)
			{
				worldMatrix = Matrix4.Identity;
				return null;
			}

			#region Vertices
			// mesh has no vertices - so it cannot be previewed
			if (mesh.m_Vertices == null || mesh.m_Vertices.Length == 0)
			{
				worldMatrix = Matrix4.Identity;
				return null;
			}

			// for some reason this has to be done? sometimes we just have to change the stride
			int stride = 3;
			if (mesh.m_Vertices.Length == mesh.m_VertexCount * 4)
			{
				stride = 4;
			}

			var positionArray = new Vector3[mesh.m_VertexCount];

			// calculate the bounding bounds for the mesh so that scale can be adjusted accordingly
			Vector3 min = new Vector3();
			Vector3 max = new Vector3();

			for (int i = 0; i < 3; i++)
			{
				min[i] = mesh.m_Vertices[i];
				max[i] = mesh.m_Vertices[i];
			}

			for (int v = 0; v < mesh.m_VertexCount; v++)
			{
				// update mesh bounds
				for (int i = 0; i < 3; i++)
				{
					min[i] = Math.Min(min[i], mesh.m_Vertices[v * stride + i]);
					max[i] = Math.Max(max[i], mesh.m_Vertices[v * stride + i]);
				}

				// update vertex data vector
				positionArray[v] = new Vector3(
					mesh.m_Vertices[v * stride],
					mesh.m_Vertices[v * stride + 1],
					mesh.m_Vertices[v * stride + 2]);
			}

			// calculate model matrix
			Vector3 dist = Vector3.One, offset = Vector3.Zero;
			for (int i = 0; i < 3; i++)
			{
				dist[i] = max[i] - min[i];
				offset[i] = (max[i] + min[i]) / 2;
			}
			float d = Math.Max(1e-5f, dist.Length);
			worldMatrix = Matrix4.CreateTranslation(-offset) * Matrix4.CreateScale(2f / d);
			#endregion

			#region Indicies
			var indexArray = new int[mesh.m_Indices.Count];
			for (int i = 0; i < mesh.m_Indices.Count; i = i + 3)
			{
				indexArray[i + 0] = (int)mesh.m_Indices[i + 0];
				indexArray[i + 1] = (int)mesh.m_Indices[i + 1];
				indexArray[i + 2] = (int)mesh.m_Indices[i + 2];
			}
			#endregion

			#region Normals
			bool normalsCalculated = false;
			var normalArray = new Vector3[mesh.m_VertexCount];

			if (mesh.m_Normals != null && mesh.m_Normals.Length > 0)
			{
				if (mesh.m_Normals.Length == mesh.m_VertexCount * 3)
				{
					stride = 3;
				}
				else if (mesh.m_Normals.Length == mesh.m_VertexCount * 4)
				{
					stride = 4;
				}
				else
				{
					stride = 0;
				}

				if (stride != 0)
				{

					for (int n = 0; n < mesh.m_VertexCount; n++)
					{
						normalArray[n] = new Vector3(
							mesh.m_Normals[n * stride],
							mesh.m_Normals[n * stride + 1],
							mesh.m_Normals[n * stride + 2]);
					}

					normalsCalculated = true;
				}
			}

			if (!normalsCalculated)
			{
				// calculate normals numerically using the geometry
				for (var i = 0; i < mesh.m_VertexCount; ++i)
				{
					normalArray[i] = Vector3.Zero;
				}

				// for each face calculate normals
				for (int i = 0; i < indexArray.Length; i = i + 3)
				{
					var i0 = indexArray[i + 0];
					var i1 = indexArray[i + 1];
					var i2 = indexArray[i + 2];

					var v0 = positionArray[i0];
					var v1 = positionArray[i1];
					var v2 = positionArray[i2];

					var d01 = v1 - v0;
					var d02 = v2 - v0;
					Vector3 faceNorm = Vector3.Cross(d01, d02);
					faceNorm.Normalize();

					for (int j = 0; j < 3; j++)
					{
						normalArray[i0 + j] += faceNorm;
					}
				}

				// normalize all normals
				for (var i = 0; i < normalArray.Length; ++i)
				{
					if (normalArray[i].LengthSquared == 0.0f)
					{
						continue;
					}
					else
					{
						normalArray[i].Normalize();
					}
				}
			}

			#endregion

			#region Colors
			var colorArray = new Vector4[mesh.m_VertexCount];

			if (mesh.m_Colors != null && mesh.m_Colors.Length == mesh.m_VertexCount * 3)
			{
				for (int c = 0; c < mesh.m_VertexCount; c++)
				{
					colorArray[c] = new Vector4(
						mesh.m_Colors[c * 3],
						mesh.m_Colors[c * 3 + 1],
						mesh.m_Colors[c * 3 + 2],
						1.0f);
				}
			}
			else if (mesh.m_Colors != null && mesh.m_Colors.Length == mesh.m_VertexCount * 4)
			{
				for (int c = 0; c < mesh.m_VertexCount; c++)
				{
					colorArray[c] = new Vector4(
						mesh.m_Colors[c * 4],
						mesh.m_Colors[c * 4 + 1],
						mesh.m_Colors[c * 4 + 2],
						mesh.m_Colors[c * 4 + 3]);
				}
			}
			else
			{
				for (int c = 0; c < mesh.m_VertexCount; c++)
				{
					colorArray[c] = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);
				}
			}
			#endregion

			return new StaticMesh(positionArray, colorArray, normalArray, indexArray);
		}
	}
}
