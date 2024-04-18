using AssetStudio;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Vector2 = OpenTK.Mathematics.Vector2;
using Vector3 = OpenTK.Mathematics.Vector3;
using Vector4 = OpenTK.Mathematics.Vector4;

namespace AssetStudioGUI
{
	internal class DisplayModel : IDisposable
	{
		class Material : IDisposable
		{
			private int m_diffuseId;

			public Material()
			{
				m_diffuseId = 0;
			}

			public void Dispose()
			{
				if (m_diffuseId != 0)
				{
					GL.DeleteTexture(m_diffuseId);
					m_diffuseId = 0;
				}
			}

			public void Bind()
			{
				if (m_diffuseId != 0)
				{
					GL.ActiveTexture(TextureUnit.Texture0);
					GL.BindTexture(TextureTarget.Texture2D, m_diffuseId);
				}
			}

			public void AttachDiffuse(int width, int height, byte[] data)
			{
				m_diffuseId = GL.GenTexture();
				GL.BindTexture(TextureTarget.Texture2D, m_diffuseId);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, width, height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgb, PixelType.UnsignedByte, data);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
				GL.BindTexture(TextureTarget.Texture2D, 0);
			}
		}

		class ModelMesh : IDisposable
		{
			private int m_vbPosition, m_vbUv, m_vbNormal, m_vbColor, m_vbIndex;
			private int m_vao;
			private int m_numIndices;

			private Material m_material;

			public ModelMesh(Material material, Vector3[] positions, Vector2[] uvs, Vector3[] normals, Vector4[] colors, int[] indices)
			{
				m_material = material;

				m_vao = GL.GenVertexArray();
				GL.BindVertexArray(m_vao);

				Renderer.CreateVBO(out m_vbPosition, positions, Renderer.SHADER_ATTRIB_POSITION);
				Renderer.CreateVBO(out m_vbNormal, normals, Renderer.SHADER_ATTRIB_NORMAL);
				Renderer.CreateVBO(out m_vbColor, colors, Renderer.SHADER_ATTRIB_COLOR);
				Renderer.CreateVBO(out m_vbUv, uvs, Renderer.SHADER_ATTRIB_UV);
				Renderer.CreateEBO(out m_vbIndex, indices);

				GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
				GL.BindVertexArray(0);

				m_numIndices = indices.Length;
			}

			public void Dispose()
			{
				GL.DeleteVertexArray(m_vao);
				GL.DeleteBuffer(m_vbPosition);
				GL.DeleteBuffer(m_vbNormal);
				GL.DeleteBuffer(m_vbColor);
				GL.DeleteBuffer(m_vbIndex);
				GL.DeleteBuffer(m_vbUv);
			}

			public void Bind()
			{
				GL.BindVertexArray(m_vao);

				if (m_material != null)
				{
					m_material.Bind();
				}
			}

			public void Draw()
			{
				GL.DrawElements(BeginMode.Triangles, m_numIndices, DrawElementsType.UnsignedInt, 0);
			}

			public static ModelMesh FromImportedMesh(Material material, ImportedMesh mesh, ImportedSubmesh submesh)
			{
				// TODO: what if model has no normals? (cope)
				var numVertices = mesh.VertexList.Count;

				var positions = new Vector3[numVertices];
				var uvs = new Vector2[numVertices];
				var normals = new Vector3[numVertices];
				var colors = new Vector4[numVertices];
				var indices = new int[submesh.FaceList.Count * 3];

				for (int i = 0; i < numVertices; i++)
				{
					var vertex = mesh.VertexList[i];

					positions[i].X = vertex.Vertex.X;
					positions[i].Y = vertex.Vertex.Y;
					positions[i].Z = vertex.Vertex.Z;

					normals[i].X = vertex.Normal.X;
					normals[i].Y = vertex.Normal.Y;
					normals[i].Z = vertex.Normal.Z;

					colors[i].X = vertex.Color.R;
					colors[i].Y = vertex.Color.G;
					colors[i].Z = vertex.Color.B;
					colors[i].W = vertex.Color.A;

					// TODO: support other texture coordinates in display mode
					uvs[i].X = vertex.UV[0][0];
					uvs[i].Y = vertex.UV[0][1];
				}

				for (int f = 0; f < submesh.FaceList.Count; ++f)
				{
					if (submesh.FaceList[f].VertexIndices.Length != 3)
					{
						throw new Exception("non triangular mesh detected");
					}

					indices[3 * f + 0] = submesh.FaceList[f].VertexIndices[0] + submesh.BaseVertex;
					indices[3 * f + 1] = submesh.FaceList[f].VertexIndices[1] + submesh.BaseVertex;
					indices[3 * f + 2] = submesh.FaceList[f].VertexIndices[2] + submesh.BaseVertex;
				}

				return new ModelMesh(material, positions, uvs, normals, colors, indices);
			}
		}

		class ModelNode
		{
			public string name { get; set; }
			public Matrix4 transform { get; set; }

			public List<ModelMesh> meshes { get; set; }
			public List<ModelNode> children { get; set; }

			public ModelNode(string name)
			{
				this.name = name;
				transform = Matrix4.Identity;

				children = new List<ModelNode>();
				meshes = new List<ModelMesh>();
			}

			public void Draw(IRenderState state, Matrix4 parentMatrix)
			{
				//Matrix4 worldMatrix = transform * parentMatrix;
				var worldMatrix = Matrix4.Identity;

				foreach (var submesh in meshes)
				{
					state.SetWorldMatrix(ref worldMatrix);
					state.SetEnableDiffuse(true);

					submesh.Bind();
					submesh.Draw();
				}

				foreach (var child in children)
				{
					child.Draw(state, worldMatrix);
				}
			}

			public void Dispose()
			{
				foreach (var submesh in meshes)
				{
					submesh.Dispose();
				}

				meshes.Clear();
				foreach (var child in children)
				{
					child.Dispose();
				}
			}
		}

		// model class implemantation starts here
		List<KeyValuePair<string, Material>> m_materials;
		ModelNode m_rootNode;

		private DisplayModel()
		{
			m_materials = new List<KeyValuePair<string, Material>>();
			m_rootNode = null;
		}

		public void Draw(IRenderState state, Matrix4 modelMatrix)
		{
			if (m_rootNode != null)
			{
				m_rootNode.Draw(state, modelMatrix);
			}
		}

		public void Dispose()
		{
			if (m_rootNode != null)
			{
				m_rootNode.Dispose();
				m_rootNode = null;
			}
		}

		private static Vector3 AssetStudioVecToOpenTK(AssetStudio.Vector3 vector)
		{
			return new Vector3(vector.X, vector.Y, vector.Z);
		}

		public static DisplayModel FromAnimator(Animator animator)
		{
			var convert = new ModelConverter(animator, Properties.Settings.Default.convertType);

			Stack<ImportedFrame> nodeStack = new Stack<ImportedFrame>();
			Stack<ModelNode> parentStack = new Stack<ModelNode>();

			nodeStack.Push(convert.RootFrame);

			DisplayModel model = new DisplayModel();

			while (nodeStack.Count > 0)
			{
				var frame = nodeStack.Pop();

				ModelNode parentNode = null;
				if (parentStack.Count > 0)
				{
					parentNode = parentStack.Pop();
				}

				var importedMesh = ImportedHelpers.FindMesh(frame.Path, convert.MeshList);

				Debug.WriteLine($"Parsing frame {frame.Name}...");

				var node = new ModelNode(frame.Name);
				var translation = AssetStudioVecToOpenTK(frame.LocalPosition);
				var rotation = AssetStudioVecToOpenTK(frame.LocalRotation);
				var scale = AssetStudioVecToOpenTK(frame.LocalScale);

				var localMatrix = 
					Matrix4.CreateTranslation(translation) * 
					Matrix4.CreateRotationX(rotation.X) * 
					Matrix4.CreateRotationY(rotation.Y) * 
					Matrix4.CreateRotationZ(rotation.Z) * 
					Matrix4.CreateScale(scale);
				node.transform = localMatrix;

				if (importedMesh != null)
				{
					foreach (var importedSubmesh in importedMesh.SubmeshList)
					{
						Material material = null;
						var importedMaterial = ImportedHelpers.FindMaterial(importedSubmesh.Material, convert.MaterialList);
						
						if (importedMaterial != null)
						{
							var prevMaterial = model.m_materials.FindIndex(kv => kv.Key == importedMaterial.Name);
							
							if (prevMaterial >= 0)
							{
								material = model.m_materials[prevMaterial].Value;
							} 
							else if (importedMaterial.Textures.Count != 0)
							{
								material = new Material();
								
								var diffuseName = importedMaterial.Textures[0].Name;
								var diffuseMap = ImportedHelpers.FindTexture(diffuseName, convert.TextureList);

								// TODO: bad coding practice
								Bitmap srcBitmap;
								using (var ms = new MemoryStream(diffuseMap.Data))
								{
									srcBitmap = new Bitmap(ms);
								}

								var bitmap = new Bitmap(srcBitmap.Width, srcBitmap.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

								using (Graphics gr = Graphics.FromImage(bitmap))
								{
									gr.DrawImage(srcBitmap, new Rectangle(0, 0, srcBitmap.Width, srcBitmap.Height));
								}

								BitmapData bmData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), 
									ImageLockMode.ReadOnly, bitmap.PixelFormat);

								var stride = bmData.Stride;
								byte[] data = new byte[stride * bitmap.Height];
								Marshal.Copy(bmData.Scan0, data, 0, data.Length);
								bitmap.UnlockBits(bmData);

								for (int i = 0; i < data.Length; i = i + 3)
								{
									var b = data[i + 0];
									var g = data[i + 1];
									var r = data[i + 2];

									data[i + 0] = r;
									data[i + 1] = g;
									data[i + 2] = b;
								}

								material.AttachDiffuse(bitmap.Width, bitmap.Height, data);

								srcBitmap.Dispose();
								bitmap.Dispose();
							}
						}

						node.meshes.Add(ModelMesh.FromImportedMesh(material, importedMesh, importedSubmesh));
					}
				}

				if (parentNode != null)
				{
					parentNode.children.Add(node);
				}
				else
				{
					model.m_rootNode = node;
				}

				for (var i = frame.Count - 1; i >= 0; i -= 1)
				{
					var child = frame[i];
					nodeStack.Push(child);
					parentStack.Push(node);
				}
			}

			return model;
		}
	}
}
