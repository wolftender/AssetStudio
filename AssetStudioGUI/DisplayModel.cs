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
using Quaternion = OpenTK.Mathematics.Quaternion;

namespace AssetStudioGUI
{
	internal class DisplayModel : IDisposable
	{
		public class KeyframedAnimation
		{
			private struct TranslationKeyframe
			{
				public float time;
				public Vector3 translation;

				public TranslationKeyframe(float t, Vector3 v)
				{
					time = t;
					translation = v;
				}
			}

			private struct RotationKeyframe
			{
				public float time;
				public Quaternion rotation;

				public RotationKeyframe(float t, Vector3 v)
				{
					time = t;
					rotation = 
						Quaternion.FromAxisAngle(new Vector3(0, 0, 1), v.Z) *
						Quaternion.FromAxisAngle(new Vector3(0, 1, 0), v.Y) *
						Quaternion.FromAxisAngle(new Vector3(1, 0, 0), v.X);
				}
			}

			private struct ScaleKeyframe
			{
				public float time;
				public Vector3 scale;

				public ScaleKeyframe(float t, Vector3 v)
				{
					time = t;
					scale = v;
				}
			}

			private struct AnimationChannel
			{
				public int refNodeId;

				public List<TranslationKeyframe> translations;
				public List<RotationKeyframe> rotations;
				public List<ScaleKeyframe> scales;

				public AnimationChannel(int refNodeId)
				{
					this.refNodeId = refNodeId;
					translations = new List<TranslationKeyframe>();
					rotations = new List<RotationKeyframe>();
					scales = new List<ScaleKeyframe>();
				}
			}

			private List<AnimationChannel> m_channels;

			private KeyframedAnimation()
			{
				m_channels = new List<AnimationChannel>();
			}

			public void ApplyTo(IModelPose pose, float t)
			{
				foreach (var channel in m_channels)
				{
					Vector3 translation = Vector3.Zero, scale = Vector3.Zero;
					Quaternion rotation = Quaternion.FromEulerAngles(Vector3.Zero);

					var numTranslations = channel.translations.Count;
					var numRotations = channel.rotations.Count;
					var numScalings = channel.scales.Count;

					for (var i = 0; i < numTranslations; i++)
					{
						var current = channel.translations[i];
						var next = channel.translations[(i + 1) % numTranslations];

						if (current.time <= t &&  next.time >= t)
						{
							float param = t / (next.time - current.time);
							translation = Vector3.Lerp(current.translation, next.translation, param);
						}
					}

					for (var i = 0; i < numRotations; i++)
					{
						var current = channel.rotations[i];
						var next = channel.rotations[(i + 1) % numRotations];

						if (current.time <= t && next.time >= t)
						{
							float param = t / (next.time - current.time);
							rotation = Quaternion.Slerp(current.rotation, next.rotation, param);
						}
					}

					for (var i = 0; i < numScalings; i++)
					{
						var current = channel.scales[i];
						var next = channel.scales[(i + 1) % numScalings];

						if (current.time <= t && next.time >= t)
						{
							float param = t / (next.time - current.time);
							scale = Vector3.Lerp(current.scale, next.scale, param);
						}
					}

					Matrix4 transform =
						Matrix4.CreateScale(scale) *
						Matrix4.CreateFromQuaternion(rotation) *
						Matrix4.CreateTranslation(translation);

					pose.SetTransform(channel.refNodeId, transform);
				}
			}

			public static KeyframedAnimation FromImportedAnimation(IModelPose pose, ImportedKeyframedAnimation animation)
			{
				KeyframedAnimation result = new KeyframedAnimation();

				foreach (var track in animation.TrackList)
				{
					var refNodeId = pose.GetIdFromPath(track.Path);
					if (refNodeId < 0)
					{
						throw new Exception($"animation does not match pose, missing path {track.Path}");
					}

					Debug.WriteLine($"animation references node {track.Path} with id {refNodeId}");

					AnimationChannel channel = new AnimationChannel(refNodeId);
					
					foreach (var keyframe in track.Translations)
					{
						channel.translations.Add(new TranslationKeyframe(keyframe.time, AssetStudioVecToOpenTK(keyframe.value)));
					}

					foreach (var keyframe in track.Rotations)
					{
						channel.rotations.Add(new RotationKeyframe(keyframe.time, AssetStudioVecToOpenTK(keyframe.value)));
					} 

					foreach (var keyframe in track.Scalings)
					{
						channel.scales.Add(new ScaleKeyframe(keyframe.time, AssetStudioVecToOpenTK(keyframe.value)));
					}

					result.m_channels.Add(channel);

					channel.translations.Sort((a, b) => a.time.CompareTo(b.time));
					channel.rotations.Sort((a, b) => a.time.CompareTo(b.time));
					channel.scales.Sort((a, b) => a.time.CompareTo(b.time));
				}

				return result;
			}
		}

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

		public interface IModelPose
		{
			void SetBindPose();
			void SetTransform(int nodeId, Matrix4 transform);

			int GetIdFromPath(string path);
			int GetParent(int nodeId);

			Matrix4 GetTransform(int nodeId);
			Matrix4[] GetCurrentPose();
		}

		class ModelPose : ICloneable, IModelPose
		{
			private Dictionary<string, int> m_boneIdByPath;
			private Matrix4[] m_transformById;

			private int[] m_parentById;
			private int m_numNodes;

			public int NumNodes
			{
				get { return m_numNodes; }
			}

			public ModelPose()
			{
				m_boneIdByPath = new Dictionary<string, int>();
				m_numNodes = 0;

				m_transformById = new Matrix4[Renderer.SHADER_MAX_BONES];
				m_parentById = new int[Renderer.SHADER_MAX_BONES];

				SetBindPose();
			}

			public int InsertNode(int parent, string path)
			{
				if (m_numNodes > Renderer.SHADER_MAX_BONES)
				{
					throw new Exception($"cannot exceed {Renderer.SHADER_MAX_BONES} bones in a model");
				}

				int nodeId = m_numNodes;
				m_numNodes++;

				m_transformById[nodeId] = Matrix4.Identity;
				m_parentById[nodeId] = parent;
				m_boneIdByPath.Add(path, nodeId);

				return nodeId;
			}

			public object Clone()
			{
				var pose = new ModelPose();

				pose.m_transformById = (Matrix4[]) m_transformById.Clone();
				pose.m_parentById = (int[]) m_parentById.Clone();
				pose.m_boneIdByPath = new Dictionary<string, int>();

				foreach (var entry in m_boneIdByPath)
				{
					pose.m_boneIdByPath[entry.Key] = entry.Value;
				}

				pose.m_numNodes = m_numNodes;
				return pose;
			}

			public void SetBindPose()
			{
				for (int i = 0; i < m_numNodes; ++i)
				{
					m_transformById[i] = Matrix4.Identity;
				}
			}

			public void SetTransform(int nodeId, Matrix4 transform)
			{
				if (nodeId > 0 && nodeId < m_numNodes)
				{
					m_transformById[nodeId] = transform;
				}
			}

			public int GetIdFromPath(string path)
			{
				if (path == null)
				{
					return 0;
				}

				if (m_boneIdByPath.ContainsKey(path))
				{
					return m_boneIdByPath[path];
				}

				return -1;
			}

			public int GetParent(int nodeId)
			{
				if (nodeId > 0 && nodeId < m_numNodes)
				{
					return m_parentById[nodeId];
				}

				return -1;
			}

			public Matrix4[] GetCurrentPose()
			{
				// recalculate current pose
				Stack<int> nodeStack = new Stack<int>();

				Matrix4[] pose = new Matrix4[m_numNodes];
				bool[] calculated = new bool[m_numNodes];

				for (int i = 0; i < m_numNodes; ++i)
				{
					calculated[i] = false;
					nodeStack.Push(i);
				}

				while (nodeStack.Count > 0)
				{
					int nodeId = nodeStack.Pop();

					if (calculated[nodeId])
					{
						continue;
					}

					var parent = GetParent(nodeId);
					if (parent < 0)
					{
						pose[nodeId] = m_transformById[nodeId];
						calculated[nodeId] = true;
					} 
					else
					{
						if (calculated[parent])
						{
							pose[nodeId] = pose[parent] * m_transformById[nodeId];
							calculated[nodeId] = true;
						} 
						else
						{
							nodeStack.Push(nodeId);
							nodeStack.Push(parent);
						}
					}
				}

				return pose;
			}

			public Matrix4 GetTransform(int nodeId)
			{
				if (nodeId > 0 && nodeId < m_numNodes)
				{
					return m_transformById[nodeId];
				}

				return Matrix4.Identity;
			}
		}

		class ModelMesh : IDisposable
		{
			private int m_vbPosition, m_vbUv, m_vbNormal, m_vbColor, m_vbIndex, m_vbBoneWeight, m_vbBoneIdx;
			private int m_vao;
			private int m_numIndices;

			private Material m_material;
			private Matrix4[] m_offsets;

			public ModelMesh(
				Material material, Vector3[] positions, 
				Vector2[] uvs, Vector3[] normals, 
				Vector4[] colors, Vector4[] boneWeights, 
				Vector4i[] boneIndices, int[] indices)
			{
				m_material = material;

				m_vao = GL.GenVertexArray();
				GL.BindVertexArray(m_vao);

				Renderer.CreateVBO(out m_vbPosition, positions, Renderer.SHADER_ATTRIB_POSITION);
				Renderer.CreateVBO(out m_vbNormal, normals, Renderer.SHADER_ATTRIB_NORMAL);
				Renderer.CreateVBO(out m_vbColor, colors, Renderer.SHADER_ATTRIB_COLOR);
				Renderer.CreateVBO(out m_vbUv, uvs, Renderer.SHADER_ATTRIB_UV);
				Renderer.CreateVBO(out m_vbBoneWeight, boneWeights, Renderer.SHADER_ATTRIB_BONEWEIGHT);
				Renderer.CreateVBO(out m_vbBoneIdx, boneIndices, Renderer.SHADER_ATTRIB_BONEIDX);
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
				GL.DeleteBuffer(m_vbBoneWeight);
				GL.DeleteBuffer(m_vbBoneIdx);
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

			public void Draw(IRenderState state, Matrix4[] pose)
			{
				var boneMatrices = new Matrix4[pose.Length];

				for (int i = 0; i < boneMatrices.Length; ++i)
				{
					boneMatrices[i] = pose[i] * m_offsets[i];
				}

				state.SetEnableSkinning(true);
				state.SetPose(boneMatrices);
				Draw();
			}

			public static ModelMesh FromImportedMesh(ModelPose pose, Material material, ImportedMesh mesh, ImportedSubmesh submesh)
			{
				// TODO: what if model has no normals? (cope)
				var numVertices = mesh.VertexList.Count;

				var positions = new Vector3[numVertices];
				var uvs = new Vector2[numVertices];
				var normals = new Vector3[numVertices];
				var colors = new Vector4[numVertices];
				var boneweights = new Vector4[numVertices];
				var boneidx = new Vector4i[numVertices];
				var indices = new int[submesh.FaceList.Count * 3];

				// create lookup table for bones
				// relation for bones is a bit complicated in this place
				// a mesh has its own list of bones, but those are merely references to nodes in model hierarchy
				// since we want only one uniform buffer per model we need to translate "mesh" bone id to "model" bone id
				var boneOffsets = new Matrix4[pose.NumNodes];

				var nodeByBone = new int[mesh.BoneList.Count];
				for (int i = 0; i < nodeByBone.Length; i++)
				{
					var path = mesh.BoneList[i].Path;
					int refNodeId = pose.GetIdFromPath(path);
					if (refNodeId < 0)
					{
						throw new Exception($"bone references invalid node path {path}");
					}

					nodeByBone[i] = refNodeId;

					// copy offset matrix
					for (int c = 0; c < 4; ++c)
					{
						for (int r = 0; r < 4; ++r)
						{
							boneOffsets[refNodeId][c,r] = mesh.BoneList[i].Matrix[c,r];
						}
					}

					//boneOffsets[refNodeId].Invert();
				}

				// fill out buffers that are going to be uploaded to the gpu
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

					if (vertex.BoneIndices.Length > 4)
					{
						throw new Exception("vertex with more than 4 bones assigned");
					}

					for (int j = 0; j < 4; ++j)
					{
						if (j < vertex.BoneIndices.Length)
						{
							boneweights[i][j] = vertex.Weights[j];
							boneidx[i][j] = nodeByBone[vertex.BoneIndices[j]]; // translate to "model" bone id
						} 
						else
						{
							boneweights[i][j] = 0.0f;
							boneidx[i][j] = 0;
						}
					}
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

				var result = new ModelMesh(material, positions, uvs, normals, colors, boneweights, boneidx, indices);
				result.m_offsets = boneOffsets;

				return result;
			}
		}

		class ModelNode
		{
			public string name { get; set; }
			public Matrix4 transform { get; set; }

			public List<ModelMesh> meshes { get; set; }
			public List<ModelNode> children { get; set; }

			private int m_id;

			public int Id
			{
				get { return m_id; }
			}

			public ModelNode(int nodeId, string name)
			{
				m_id = nodeId;

				this.name = name;
				transform = Matrix4.Identity;

				children = new List<ModelNode>();
				meshes = new List<ModelMesh>();
			}

			public void Draw(IRenderState state, Matrix4 parentMatrix)
			{
				Matrix4 worldMatrix = transform * parentMatrix;

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

			public void Draw(IRenderState state, Matrix4[] currentPose, Matrix4 parentMatrix)
			{
				Matrix4 worldMatrix = transform * parentMatrix;

				foreach (var submesh in meshes)
				{
					state.SetWorldMatrix(ref worldMatrix);
					state.SetEnableDiffuse(true);

					submesh.Bind();
					submesh.Draw(state, currentPose);
				}

				foreach (var child in children)
				{
					child.Draw(state, currentPose, worldMatrix);
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
		ModelPose m_pose;

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

		public void Draw(IRenderState state, IModelPose pose, Matrix4 modelMatrix)
		{
			if (m_rootNode != null)
			{
				var currentPose = pose.GetCurrentPose();
				m_rootNode.Draw(state, currentPose, modelMatrix);
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

		public IModelPose CreatePose()
		{
			return (IModelPose)m_pose.Clone();
		}

		private static Vector3 AssetStudioVecToOpenTK(AssetStudio.Vector3 vector)
		{
			return new Vector3(vector.X, vector.Y, vector.Z);
		}

		public static DisplayModel FromAnimatorAndClip(Animator animator, AnimationClip clip, out KeyframedAnimation animation)
		{
			var animClips = new AnimationClip[] { clip };
			var convert = new ModelConverter(animator, Properties.Settings.Default.convertType, animClips);

			var model = FromConverter(convert);
			animation = KeyframedAnimation.FromImportedAnimation(model.CreatePose(), convert.AnimationList.First());

			return model;
		}

		public static DisplayModel FromAnimator(Animator animator)
		{
			var convert = new ModelConverter(animator, Properties.Settings.Default.convertType);
			return FromConverter(convert);
		}

		private static DisplayModel FromConverter(ModelConverter convert)
		{
			Stack<ImportedFrame> nodeStack = new Stack<ImportedFrame>();
			Stack<ModelNode> parentStack = new Stack<ModelNode>();

			nodeStack.Push(convert.RootFrame);

			DisplayModel model = new DisplayModel();
			List<KeyValuePair<ModelNode, ImportedMesh>> meshLoadList = new List<KeyValuePair<ModelNode, ImportedMesh>>();

			model.m_pose = new ModelPose();

			while (nodeStack.Count > 0)
			{
				var frame = nodeStack.Pop();

				// pop parent from the stack, if no parent then root node
				// probably should assert only one root exists?
				ModelNode parentNode = null;
				if (parentStack.Count > 0)
				{
					parentNode = parentStack.Pop();
				}

				var importedMesh = ImportedHelpers.FindMesh(frame.Path, convert.MeshList);

				Debug.WriteLine($"Parsing frame {frame.Name}...");

				int parentId = -1;
				if (parentNode != null)
				{
					parentId = parentNode.Id;
				}

				var nodeId = model.m_pose.InsertNode(parentId, frame.Path);
				var node = new ModelNode(nodeId, frame.Name);

				// node transform
				var translation = AssetStudioVecToOpenTK(frame.LocalPosition);
				var rotation = AssetStudioVecToOpenTK(frame.LocalRotation);
				var scale = AssetStudioVecToOpenTK(frame.LocalScale);

				var localMatrix =
					Matrix4.CreateScale(scale) *
					Matrix4.CreateRotationZ(rotation.Z) *
					Matrix4.CreateRotationY(rotation.Y) *
					Matrix4.CreateRotationX(rotation.X) *
					Matrix4.CreateTranslation(translation);
				node.transform = localMatrix;

				// add mesh to load list if exists
				if (importedMesh != null)
				{
					meshLoadList.Add(new KeyValuePair<ModelNode, ImportedMesh>(node, importedMesh));
				}

				// if this is a child node then add it to the parent
				if (parentNode != null)
				{
					parentNode.children.Add(node);
				}
				else
				{
					model.m_rootNode = node; // not a child, so set as root
				}

				// push all children on the stack, push self as parent for the children on the stack
				for (var i = frame.Count - 1; i >= 0; i -= 1)
				{
					var child = frame[i];
					nodeStack.Push(child);
					parentStack.Push(node);
				}
			}

			// model frame is initialized, now we can initialize the meshes
			// this is done because we need the WHOLE frame to initialize bones
			foreach (var pair in meshLoadList)
			{
				var node = pair.Key;
				var importedMesh = pair.Value;

				// mesh is built from submeshes
				// for rendering each submesh is simply a component VAO with own vertex buffers
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

							// TODO: bad coding practice (probably)
							Bitmap srcBitmap;
							using (var ms = new MemoryStream(diffuseMap.Data))
							{
								srcBitmap = new Bitmap(ms);
							}

							// convert bitmap to correct pixel format for opengl
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

							// for some reason bitmap class returns bytes in order BRG instead of RGB, so we need to swap them for opengl
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

					node.meshes.Add(ModelMesh.FromImportedMesh(model.m_pose, material, importedMesh, importedSubmesh));
				}
			}

			return model;
		}
	}
}
