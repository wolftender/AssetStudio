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
	interface IRenderState
	{
		void SetPose(Matrix4[] pose);
		void ResetPose();
		void SetDiffuseMap(int texId);
		void SetEnableDiffuse(bool enableDiffuse);
		void SetWorldMatrix(ref Matrix4 worldMatrix);
		void SetViewMatrix(ref Matrix4 viewMatrix);
		void SetProjMatrix(ref Matrix4 projMatrix);
		void SetCameraPosition(ref Vector3 position);
		void SetEnableSkinning(bool enableSkinning);
	}

	internal class Renderer : IDisposable
	{
		interface IDrawable : IDisposable
		{
			void Draw(IRenderState state, Matrix4 worldMatrix);
		}

		class DrawableStaticMesh : IDrawable
		{
			private readonly StaticMesh m_mesh;

			public DrawableStaticMesh(StaticMesh mesh)
			{
				m_mesh = mesh;
			}

			public void Dispose()
			{
				m_mesh.Dispose();
			}

			public void Draw(IRenderState state, Matrix4 worldMatrix)
			{
				//GL.UniformMatrix4(uWorldMatrix, false, ref worldMatrix);

				state.SetWorldMatrix(ref worldMatrix);
				state.SetEnableDiffuse(false);
				state.SetEnableSkinning(false);

				m_mesh.Bind();
				m_mesh.Draw();
			}
		}

		class DrawableDisplayModel : IDrawable
		{
			private readonly DisplayModel m_mesh;

			public DrawableDisplayModel(DisplayModel mesh)
			{
				m_mesh = mesh;
			}

			public void Dispose()
			{
				m_mesh.Dispose();
			}

			public void Draw(IRenderState state, Matrix4 worldMatrix)
			{
				state.SetWorldMatrix(ref worldMatrix);
				state.SetEnableDiffuse(false);
				state.SetEnableSkinning(true);
				state.ResetPose();

				m_mesh.Draw(state, worldMatrix);
			}
		}

		class RenderStateImpl : IRenderState, IDisposable
		{
			public static readonly int UBO_BINDING_BONES = 0;

			private int m_uModelWorld, m_uModelView, m_uModelProj,
			m_uCamPos, m_uDiffuseMap, m_uEnableDiffuseMap, m_uEnableSkinning;

			private int m_boneBuffer;

			public RenderStateImpl(int modelShader)
			{
				m_uModelWorld = GL.GetUniformLocation(modelShader, "u_world");
				m_uModelView = GL.GetUniformLocation(modelShader, "u_view");
				m_uModelProj = GL.GetUniformLocation(modelShader, "u_projection");
				m_uCamPos = GL.GetUniformLocation(modelShader, "u_cam_position");
				m_uDiffuseMap = GL.GetUniformLocation(modelShader, "u_diffuse_map");
				m_uEnableDiffuseMap = GL.GetUniformLocation(modelShader, "u_enable_diffuse_map");
				m_uEnableSkinning = GL.GetUniformLocation(modelShader, "u_enable_skinning");

				// setup bone buffer
				int boneBufferSize = 16 * 4 * SHADER_MAX_BONES;

				m_boneBuffer = GL.GenBuffer();
				GL.BindBuffer(BufferTarget.UniformBuffer, m_boneBuffer);
				GL.BufferData(BufferTarget.UniformBuffer, boneBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

				var bonesIndex = GL.GetUniformBlockIndex(modelShader, "BoneData");
				GL.UniformBlockBinding(modelShader, bonesIndex, UBO_BINDING_BONES);
				GL.BindBufferBase(BufferRangeTarget.UniformBuffer, UBO_BINDING_BONES, m_boneBuffer);

				GL.BindBuffer(BufferTarget.UniformBuffer, 0);
			}

			public void SetPose(Matrix4[] pose)
			{
				int numMatrices = Math.Min(pose.Length, SHADER_MAX_BONES);

				GL.BindBuffer(BufferTarget.UniformBuffer, m_boneBuffer);
				GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, 16 * 4 * numMatrices, pose);
				GL.BindBuffer(BufferTarget.UniformBuffer, 0);
			}

			public void ResetPose()
			{
				Matrix4[] pose = new Matrix4[SHADER_MAX_BONES];
				for (int i = 0; i < pose.Length; ++i)
				{
					pose[i] = Matrix4.Identity;
				}

				SetPose(pose);
			}
			
			public void SetDiffuseMap(int texId)
			{
				GL.Uniform1(m_uDiffuseMap, texId);
			}

			public void SetEnableDiffuse(bool enableDiffuse)
			{
				GL.Uniform1(m_uEnableDiffuseMap, enableDiffuse ? 1 : 0);
			}

			public void SetWorldMatrix(ref Matrix4 worldMatrix)
			{
				GL.UniformMatrix4(m_uModelWorld, false, ref worldMatrix);
			}

			public void SetViewMatrix(ref Matrix4 viewMatrix)
			{
				GL.UniformMatrix4(m_uModelView, false, ref viewMatrix);
			}

			public void SetProjMatrix(ref Matrix4 projMatrix)
			{
				GL.UniformMatrix4(m_uModelProj, false, ref projMatrix);
			}

			public void SetCameraPosition(ref Vector3 position)
			{
				GL.Uniform3(m_uCamPos, position);
			}

			public void SetEnableSkinning(bool enableSkinning)
			{
				GL.Uniform1(m_uEnableSkinning, enableSkinning ? 1 : 0);
			}

			public void Dispose()
			{
				GL.DeleteBuffer(m_boneBuffer);
				m_boneBuffer = 0;
			}
		}

		public static readonly int SHADER_ATTRIB_POSITION = 0;
		public static readonly int SHADER_ATTRIB_NORMAL = 1;
		public static readonly int SHADER_ATTRIB_UV = 2;
		public static readonly int SHADER_ATTRIB_COLOR = 3;
		public static readonly int SHADER_ATTRIB_BONEWEIGHT = 4;
		public static readonly int SHADER_ATTRIB_BONEIDX = 5;

		public static readonly int SHADER_MAX_BONES = 200;

		private GLControl m_Control;
		private Size m_Size;

		// transform matrix
		private Matrix4 m_projMatrix;
		private Matrix4 m_viewMatrix;
		private Matrix4 m_modelMatrix;

		// mesh
		private IDrawable m_mesh;

		// shaders
		private int m_ModelShader;
		private RenderStateImpl m_state;

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

		public Size BufferSize
		{
			get { return m_Size; }
		}

		public Renderer(GLControl control)
		{
			m_Control = control;
			m_ModelShader = LoadShaderProgram(
				Encoding.Default.GetString(Properties.Resources.modelShaderVS),
				Encoding.Default.GetString(Properties.Resources.modelShaderFS));

			m_state = new RenderStateImpl(m_ModelShader);

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
			if (m_mesh != null)
			{
				GL.UseProgram(m_ModelShader);

				var cameraPosVector = cameraPosition.Xyz;

				m_state.SetViewMatrix(ref m_viewMatrix);
				m_state.SetProjMatrix(ref m_projMatrix);
				m_state.SetWorldMatrix(ref m_modelMatrix);
				m_state.SetCameraPosition(ref cameraPosVector);
				m_state.SetDiffuseMap(0);

				GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

				m_mesh.Draw(m_state, m_modelMatrix);
			}

			// refresh
			GL.BindVertexArray(0);
			GL.Flush();
			
			m_Control.SwapBuffers();
		}

		// sets the model from mesh
		public bool SetModel(Mesh mesh)
		{
			var newMesh = StaticMesh.FromMesh(mesh, out m_modelMatrix);
			
			if (newMesh == null)
			{
				return false;
			}

			if (m_mesh != null)
			{
				m_mesh.Dispose();
			}

			m_mesh = new DrawableStaticMesh(newMesh);
			return true;
		}

		// sets the model from animator
		public bool SetModel(Animator animator)
		{
			var newModel = DisplayModel.FromAnimator(animator);
			if (newModel == null)
			{
				return false;
			}

			if (m_mesh != null)
			{
				m_mesh.Dispose();
			}

			m_mesh = new DrawableDisplayModel(newModel);
			return true;
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

		private void DisposeBuffers()
		{
			m_state.Dispose();
			m_state = null;

			m_mesh.Dispose();
			m_mesh = null;
		}

		public void Dispose()
		{
			DisposeBuffers();
			GL.DeleteProgram(m_ModelShader);
		}

		// helpers
		public static void CreateEBO(out int address, int[] data)
		{
			GL.GenBuffers(1, out address);
			GL.BindBuffer(BufferTarget.ElementArrayBuffer, address);
			GL.BufferData(BufferTarget.ElementArrayBuffer,
							(IntPtr)(data.Length * sizeof(int)),
							data,
							BufferUsageHint.StaticDraw);
		}

		public static void CreateVBO(out int vboAddress, Vector2[] data, int address)
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

		public static void CreateVBO(out int vboAddress, Vector3[] data, int address)
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

		public static void CreateVBO(out int vboAddress, Vector4[] data, int address)
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

		public static void CreateVBO(out int vboAddress, Vector4i[] data, int address)
		{
			GL.GenBuffers(1, out vboAddress);
			GL.BindBuffer(BufferTarget.ArrayBuffer, vboAddress);
			GL.BufferData(BufferTarget.ArrayBuffer,
									(IntPtr)(data.Length * Vector4i.SizeInBytes),
									data,
									BufferUsageHint.StaticDraw);
			GL.VertexAttribIPointer(address, 4, VertexAttribIntegerType.Int, 0, IntPtr.Zero);
			GL.EnableVertexAttribArray(address);
		}
	}
}
