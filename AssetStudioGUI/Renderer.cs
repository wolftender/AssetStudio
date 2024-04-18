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
		private GLControl m_Control;

		// transform matrix
		private Matrix4 m_projMatrix;
		private Matrix4 m_viewMatrix;
		private Matrix4 m_modelMatrix;

		// mesh
		private StaticMesh m_mesh;

		// shaders
		private int m_ModelShader;
		private int m_uModelWorld, m_uModelView, m_uModelProj, m_uCamPos, m_uDiffuseMap;

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
				m_mesh.Bind();
				GL.UseProgram(m_ModelShader);
				GL.UniformMatrix4(m_uModelWorld, false, ref m_modelMatrix);
				GL.UniformMatrix4(m_uModelView, false, ref m_viewMatrix);
				GL.UniformMatrix4(m_uModelProj, false, ref m_projMatrix);
				GL.Uniform3(m_uCamPos, cameraPosition.Xyz);
				GL.Uniform1(m_uDiffuseMap, 0);
				GL.UniformMatrix4(m_uModelProj, false, ref m_projMatrix);
				GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
				m_mesh.Draw();
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

			m_mesh = newMesh;

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

		private void DisposeBuffers()
		{
			m_mesh.Dispose();
			m_mesh = null;
		}

		public void Dispose()
		{
			DisposeBuffers();
			GL.DeleteProgram(m_ModelShader);
		}
	}
}
