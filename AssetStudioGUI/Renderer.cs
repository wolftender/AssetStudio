using AssetStudio;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Vector2 = OpenTK.Mathematics.Vector2;
using Vector3 = OpenTK.Mathematics.Vector3;
using Vector4 = OpenTK.Mathematics.Vector4;

namespace AssetStudioGUI
{
	internal class Renderer : IDisposable
	{
		private static readonly int SHADER_ATTRIB_POSITION	= 0;
		private static readonly int SHADER_ATTRIB_NORMAL	= 1;
		private static readonly int SHADER_ATTRIB_UV		= 2;
		private static readonly int SHADER_ATTRIB_COLOR		= 3;

		private GLControl m_Control;
		private Size m_Size;

		// transform matrix
		private Matrix4 m_projMatrix;
		private Matrix4 m_viewMatrix;
		private Matrix4 m_modelMatrix;

		// shaders
		private int m_ModelShader;
		private int m_uModelWorld, m_uModelView, m_uModelProj, m_uCamPos, m_uDiffuseMap;

		// buffers
		private int m_vbPosition, m_vbUv, m_vbNormal, m_vbColor, m_vbIndex;
		private int m_vao;

		// vertices num
		private int m_numIndices;

		// camera settings
		private float m_zoom, m_pitch, m_yaw;

		public float Zoom
		{
			get { return m_zoom; }
			set { m_zoom = Math.Min(Math.Max(value, 1.0f), 20.0f); }
		}

		public float Pitch
		{
			get { return m_pitch; }
			set { m_pitch = Math.Min(Math.Max(value, -1.56f), 1.56f); }
		}

		public float Yaw
		{
			get { return m_yaw; }
			set { m_yaw = value; }
		}

		public Renderer(GLControl control)
		{
			m_Control = control;
			m_ModelShader = LoadShaderProgram(
				Encoding.Default.GetString(Properties.Resources.modelShaderVS),
				Encoding.Default.GetString(Properties.Resources.modelShaderFS));

			m_uModelWorld = GL.GetUniformLocation(m_ModelShader, "u_world");
			m_uModelView = GL.GetUniformLocation(m_ModelShader, "u_view");
			m_uModelProj = GL.GetUniformLocation(m_ModelShader, "u_projection");
			m_uCamPos = GL.GetUniformLocation(m_ModelShader, "u_cam_position");
			m_uDiffuseMap = GL.GetUniformLocation(m_ModelShader, "u_diffuse_map");

			m_vao = 0;
			m_vbPosition = 0;
			m_vbUv = 0;
			m_vbNormal = 0;
			m_vbColor = 0;
			m_vbIndex = 0;

			m_pitch = 0.0f;
			m_yaw = 0.0f;
			m_zoom = 4.0f;

			m_projMatrix = Matrix4.CreatePerspectiveFieldOfView((float)Math.PI / 3.0f, (float)control.Width / control.Height, 0.1f, 100.0f);
			m_viewMatrix = Matrix4.LookAt(new Vector3(3, 3, 3), Vector3.Zero, new Vector3(0, 1, 0));
		}

		public void UpdateSize(Size newSize)
		{
			m_projMatrix = Matrix4.CreatePerspectiveFieldOfView((float)Math.PI / 3.0f, (float)newSize.Width / newSize.Height, 0.1f, 100.0f);
			GL.Viewport(0, 0, newSize.Width, newSize.Height);
			m_Size = newSize;
		}

		public void Redraw()
		{
			// rebuild camera matrix
			Vector4 cameraPosition = new Vector4(0.0f, 0.0f, -m_zoom, 0.0f);
			Matrix4 transform = Matrix4.Identity;

			transform = Matrix4.CreateRotationX(m_pitch) * transform;
			transform = Matrix4.CreateRotationY(m_yaw) * transform;
			cameraPosition = transform * cameraPosition;

			m_viewMatrix = Matrix4.LookAt(cameraPosition.Xyz, Vector3.Zero, new Vector3(0.0f, 1.0f, 0.0f));

			m_Control.MakeCurrent();
			GL.ClearColor(System.Drawing.Color.Black);
			GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
			GL.Enable(EnableCap.DepthTest);
			GL.DepthFunc(DepthFunction.Lequal);

			// render models
			if (m_vao != 0)
			{
				GL.BindVertexArray(m_vao);
				GL.UseProgram(m_ModelShader);
				GL.UniformMatrix4(m_uModelWorld, false, ref m_modelMatrix);
				GL.UniformMatrix4(m_uModelView, false, ref m_viewMatrix);
				GL.UniformMatrix4(m_uModelProj, false, ref m_projMatrix);
				GL.Uniform3(m_uCamPos, cameraPosition.Xyz);
				GL.Uniform1(m_uDiffuseMap, 0);
				GL.UniformMatrix4(m_uModelProj, false, ref m_projMatrix);
				GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
				GL.DrawElements(BeginMode.Triangles, m_numIndices, DrawElementsType.UnsignedInt, 0);
			}

			// refresh
			GL.BindVertexArray(0);
			GL.Flush();
			
			m_Control.SwapBuffers();
		}

		// sets the model from mesh
		public bool SetModel(Mesh mesh)
		{
			if (mesh.m_VertexCount <= 0)
			{
				return false;
			}

			#region Vertices
			// mesh has no vertices - so it cannot be previewed
			if (mesh.m_Vertices == null || mesh.m_Vertices.Length == 0)
			{
				return false;
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
			m_modelMatrix = Matrix4.CreateTranslation(-offset) * Matrix4.CreateScale(2f / d);
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
				for (int i = 0; i < indexArray.Length; i = i + 3) {
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

			DisposeBuffers();

			m_vao = GL.GenVertexArray();
			GL.BindVertexArray(m_vao);

			CreateVBO(out m_vbPosition, positionArray, SHADER_ATTRIB_POSITION);
			CreateVBO(out m_vbNormal, normalArray, SHADER_ATTRIB_NORMAL);
			CreateVBO(out m_vbColor, colorArray, SHADER_ATTRIB_COLOR);
			CreateEBO(out m_vbIndex, indexArray);

			GL.BindBuffer(BufferTarget.ArrayBuffer, 0);	
			GL.BindVertexArray(0);

			m_numIndices = indexArray.Length;
			return true;
		}

		// sets the model from animator
		public void SetModel(Animator animator)
		{

		}

		private static int LoadShader(ShaderType type, string source)
		{
			var shader = GL.CreateShader(type);

			GL.ShaderSource(shader, source);
			GL.CompileShader(shader);

			GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
			if (status == 0)
			{
				string log = GL.GetShaderInfoLog(shader);
				throw new Exception($"shader compile failed: {log}");
			}

			return shader;
		}

		private static int LoadShaderProgram(string vertexShaderSource, string fragmentShaderSource)
		{
			var vertexShader = LoadShader(ShaderType.VertexShader, vertexShaderSource);
			var fragmentShader = LoadShader(ShaderType.FragmentShader, fragmentShaderSource);

			var program = GL.CreateProgram();
			GL.AttachShader(program, vertexShader);
			GL.AttachShader(program, fragmentShader);
			GL.LinkProgram(program);

			GL.DeleteShader(vertexShader); 
			GL.DeleteShader(fragmentShader);

			return program;
		}

		private static void CreateEBO(out int address, int[] data)
		{
			GL.GenBuffers(1, out address);
			GL.BindBuffer(BufferTarget.ElementArrayBuffer, address);
			GL.BufferData(BufferTarget.ElementArrayBuffer,
							(IntPtr)(data.Length * sizeof(int)),
							data,
							BufferUsageHint.StaticDraw);
		}

		private static void CreateVBO(out int vboAddress, Vector2[] data, int address)
		{
			GL.GenBuffers(1, out vboAddress);
			GL.BindBuffer(BufferTarget.ArrayBuffer, vboAddress);
			GL.BufferData(BufferTarget.ArrayBuffer,
									(IntPtr)(data.Length * Vector2.SizeInBytes),
									data,
									BufferUsageHint.StaticDraw);
			GL.VertexAttribPointer(address, 2, VertexAttribPointerType.Float, false, 0, 0);
			GL.EnableVertexAttribArray(address);
		}

		private static void CreateVBO(out int vboAddress, Vector3[] data, int address)
		{
			GL.GenBuffers(1, out vboAddress);
			GL.BindBuffer(BufferTarget.ArrayBuffer, vboAddress);
			GL.BufferData(BufferTarget.ArrayBuffer,
									(IntPtr)(data.Length * Vector3.SizeInBytes),
									data,
									BufferUsageHint.StaticDraw);
			GL.VertexAttribPointer(address, 3, VertexAttribPointerType.Float, false, 0, 0);
			GL.EnableVertexAttribArray(address);
		}

		private static void CreateVBO(out int vboAddress, Vector4[] data, int address)
		{
			GL.GenBuffers(1, out vboAddress);
			GL.BindBuffer(BufferTarget.ArrayBuffer, vboAddress);
			GL.BufferData(BufferTarget.ArrayBuffer,
									(IntPtr)(data.Length * Vector4.SizeInBytes),
									data,
									BufferUsageHint.StaticDraw);
			GL.VertexAttribPointer(address, 4, VertexAttribPointerType.Float, false, 0, 0);
			GL.EnableVertexAttribArray(address);
		}

		private void DisposeBuffers()
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

		public void Dispose()
		{
			DisposeBuffers();
			GL.DeleteProgram(m_ModelShader);
		}
	}
}
