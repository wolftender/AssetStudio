﻿using AssetStudio;
using Newtonsoft.Json;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using static AssetStudioGUI.Studio;
using Font = AssetStudio.Font;
using AssetStudioGUI.Scripts;
#if NET472
using Vector3 = OpenTK.Vector3;
using Vector4 = OpenTK.Vector4;
#else
using Vector3 = OpenTK.Mathematics.Vector3;
using Vector4 = OpenTK.Mathematics.Vector4;
using Matrix4 = OpenTK.Mathematics.Matrix4;
#endif

namespace AssetStudioGUI
{
	partial class AssetStudioGUIForm : Form
	{
		private AssetItem lastSelectedItem;
		private DirectBitmap imageTexture;
		private string tempClipboard;

		private FMOD.System system;
		private FMOD.Sound sound;
		private FMOD.Channel channel;
		private FMOD.SoundGroup masterSoundGroup;
		private FMOD.MODE loopMode = FMOD.MODE.LOOP_OFF;
		private uint FMODlenms;
		private float FMODVolume = 0.8f;

		#region TexControl
		private static char[] textureChannelNames = new[] { 'B', 'G', 'R', 'A' };
		private bool[] textureChannels = new[] { true, true, true, true };
		#endregion

		#region GLControl
		private bool glControlLoaded;
		private int mdx, mdy;
		private bool lmdown, rmdown;
		#endregion

		//asset list sorting
		private int sortColumn = -1;
		private bool reverseSort;

		//asset list filter
		private System.Timers.Timer delayTimer;
		private bool enableFiltering;

		//tree search
		private int nextGObject;
		private List<TreeNode> treeSrcResults = new List<TreeNode>();

		private string openDirectoryBackup = string.Empty;
		private string saveDirectoryBackup = string.Empty;

		private GUILogger logger;

		// graphics
		private Renderer renderer;

		[DllImport("gdi32.dll")]
		private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, [In] ref uint pcFonts);

		public AssetStudioGUIForm()
		{
			Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
			InitializeComponent();
			Text = $"AssetStudioGUI v{Application.ProductVersion}";
			delayTimer = new System.Timers.Timer(800);
			delayTimer.Elapsed += new ElapsedEventHandler(delayTimer_Elapsed);
			displayAll.Checked = Properties.Settings.Default.displayAll;
			displayInfo.Checked = Properties.Settings.Default.displayInfo;
			enablePreview.Checked = Properties.Settings.Default.enablePreview;
			FMODinit();

			logger = new GUILogger(StatusStripUpdate);
			Logger.Default = logger;
			Progress.Default = new Progress<int>(SetProgressBarValue);
			Studio.StatusStripUpdate = StatusStripUpdate;
			UserScripts.StatusStripUpdate = StatusStripUpdate;

			renderer = null;
		}

		private void AssetStudioGUIForm_DragEnter(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				e.Effect = DragDropEffects.Move;
			}
		}

		private async void AssetStudioGUIForm_DragDrop(object sender, DragEventArgs e)
		{
			var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
			if (paths.Length > 0)
			{
				ResetForm();
				assetsManager.SpecifyUnityVersion = specifyUnityVersion.Text;
				if (paths.Length == 1 && Directory.Exists(paths[0]))
				{
					await Task.Run(() => assetsManager.LoadFolder(paths[0]));
				}
				else
				{
					await Task.Run(() => assetsManager.LoadFiles(paths));
				}
				BuildAssetStructures();
			}
		}

		private async void loadFile_Click(object sender, EventArgs e)
		{
			openFileDialog1.InitialDirectory = openDirectoryBackup;
			if (openFileDialog1.ShowDialog(this) == DialogResult.OK)
			{
				ResetForm();
				openDirectoryBackup = Path.GetDirectoryName(openFileDialog1.FileNames[0]);
				assetsManager.SpecifyUnityVersion = specifyUnityVersion.Text;
				await Task.Run(() => assetsManager.LoadFiles(openFileDialog1.FileNames));
				BuildAssetStructures();
			}
		}

		private async void loadFolder_Click(object sender, EventArgs e)
		{
			var openFolderDialog = new OpenFolderDialog();
			openFolderDialog.InitialFolder = openDirectoryBackup;
			if (openFolderDialog.ShowDialog(this) == DialogResult.OK)
			{
				ResetForm();
				openDirectoryBackup = openFolderDialog.Folder;
				assetsManager.SpecifyUnityVersion = specifyUnityVersion.Text;
				await Task.Run(() => assetsManager.LoadFolder(openFolderDialog.Folder));
				BuildAssetStructures();
			}
		}

		private async void extractFileToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (openFileDialog1.ShowDialog(this) == DialogResult.OK)
			{
				var saveFolderDialog = new OpenFolderDialog();
				saveFolderDialog.Title = "Select the save folder";
				if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
				{
					var fileNames = openFileDialog1.FileNames;
					var savePath = saveFolderDialog.Folder;
					var extractedCount = await Task.Run(() => ExtractFile(fileNames, savePath));
					StatusStripUpdate($"Finished extracting {extractedCount} files.");
				}
			}
		}

		private async void extractFolderToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var openFolderDialog = new OpenFolderDialog();
			if (openFolderDialog.ShowDialog(this) == DialogResult.OK)
			{
				var saveFolderDialog = new OpenFolderDialog();
				saveFolderDialog.Title = "Select the save folder";
				if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
				{
					var path = openFolderDialog.Folder;
					var savePath = saveFolderDialog.Folder;
					var extractedCount = await Task.Run(() => ExtractFolder(path, savePath));
					StatusStripUpdate($"Finished extracting {extractedCount} files.");
				}
			}
		}

		private async void BuildAssetStructures()
		{
			if (assetsManager.assetsFileList.Count == 0)
			{
				StatusStripUpdate("No Unity file can be loaded.");
				return;
			}

			(var productName, var treeNodeCollection) = await Task.Run(() => BuildAssetData());
			var typeMap = await Task.Run(() => BuildClassStructure());

			if (!string.IsNullOrEmpty(productName))
			{
				Text = $"AssetStudioGUI v{Application.ProductVersion} - {productName} - {assetsManager.assetsFileList[0].unityVersion} - {assetsManager.assetsFileList[0].m_TargetPlatform}";
			}
			else
			{
				Text = $"AssetStudioGUI v{Application.ProductVersion} - no productName - {assetsManager.assetsFileList[0].unityVersion} - {assetsManager.assetsFileList[0].m_TargetPlatform}";
			}

			assetListView.VirtualListSize = visibleAssets.Count;

			sceneTreeView.BeginUpdate();
			sceneTreeView.Nodes.AddRange(treeNodeCollection.ToArray());
			sceneTreeView.EndUpdate();
			treeNodeCollection.Clear();

			classesListView.BeginUpdate();
			foreach (var version in typeMap)
			{
				var versionGroup = new ListViewGroup(version.Key);
				classesListView.Groups.Add(versionGroup);

				foreach (var uclass in version.Value)
				{
					uclass.Value.Group = versionGroup;
					classesListView.Items.Add(uclass.Value);
				}
			}
			typeMap.Clear();
			classesListView.EndUpdate();

			var types = exportableAssets.Select(x => x.Type).Distinct().OrderBy(x => x.ToString()).ToArray();
			foreach (var type in types)
			{
				var typeItem = new ToolStripMenuItem
				{
					CheckOnClick = true,
					Name = type.ToString(),
					Size = new Size(180, 22),
					Text = type.ToString()
				};
				typeItem.Click += typeToolStripMenuItem_Click;
				filterTypeToolStripMenuItem.DropDownItems.Add(typeItem);
			}
			allToolStripMenuItem.Checked = true;
			var log = $"Finished loading {assetsManager.assetsFileList.Count} files with {assetListView.Items.Count} exportable assets";
			var m_ObjectsCount = assetsManager.assetsFileList.Sum(x => x.m_Objects.Count);
			var objectsCount = assetsManager.assetsFileList.Sum(x => x.Objects.Count);
			if (m_ObjectsCount != objectsCount)
			{
				log += $" and {m_ObjectsCount - objectsCount} assets failed to read";
			}
			StatusStripUpdate(log);
		}

		private void typeToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var typeItem = (ToolStripMenuItem)sender;
			if (typeItem != allToolStripMenuItem)
			{
				allToolStripMenuItem.Checked = false;
			}
			else if (allToolStripMenuItem.Checked)
			{
				for (var i = 1; i < filterTypeToolStripMenuItem.DropDownItems.Count; i++)
				{
					var item = (ToolStripMenuItem)filterTypeToolStripMenuItem.DropDownItems[i];
					item.Checked = false;
				}
			}
			FilterAssetList();
		}

		private void AssetStudioForm_KeyDown(object sender, KeyEventArgs e)
		{
			if (glControl1.Visible)
			{
				if (e.Control)
				{
					switch (e.KeyCode)
					{
						case Keys.W:
							//TODO: Toggle WireFrame
							//wireFrameMode = (wireFrameMode + 1) % 3;
							glControl1.Invalidate();
							break;
						case Keys.S:
							//TODO: Toggle Shade
							//shadeMode = (shadeMode + 1) % 2;
							glControl1.Invalidate();
							break;
						case Keys.N:
							//TODO: Normal mode
							//normalMode = (normalMode + 1) % 2;
							glControl1.Invalidate();
							break;
					}
				}
			}
			else if (previewPanel.Visible)
			{
				if (e.Control)
				{
					var need = false;
					switch (e.KeyCode)
					{
						case Keys.B:
							textureChannels[0] = !textureChannels[0];
							need = true;
							break;
						case Keys.G:
							textureChannels[1] = !textureChannels[1];
							need = true;
							break;
						case Keys.R:
							textureChannels[2] = !textureChannels[2];
							need = true;
							break;
						case Keys.A:
							textureChannels[3] = !textureChannels[3];
							need = true;
							break;
					}
					if (need)
					{
						if (lastSelectedItem != null)
						{
							PreviewAsset(lastSelectedItem);
							assetInfoLabel.Text = lastSelectedItem.InfoText;
						}
					}
				}
			}
		}

		private void exportClassStructuresMenuItem_Click(object sender, EventArgs e)
		{
			if (classesListView.Items.Count > 0)
			{
				var saveFolderDialog = new OpenFolderDialog();
				if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
				{
					var savePath = saveFolderDialog.Folder;
					var count = classesListView.Items.Count;
					int i = 0;
					Progress.Reset();
					foreach (TypeTreeItem item in classesListView.Items)
					{
						var versionPath = Path.Combine(savePath, item.Group.Header);
						Directory.CreateDirectory(versionPath);

						var saveFile = $"{versionPath}{Path.DirectorySeparatorChar}{item.SubItems[1].Text} {item.Text}.txt";
						File.WriteAllText(saveFile, item.ToString());

						Progress.Report(++i, count);
					}

					StatusStripUpdate("Finished exporting class structures");
				}
			}
		}

		private void displayAll_CheckedChanged(object sender, EventArgs e)
		{
			Properties.Settings.Default.displayAll = displayAll.Checked;
			Properties.Settings.Default.Save();
		}

		private void enablePreview_Check(object sender, EventArgs e)
		{
			if (lastSelectedItem != null)
			{
				switch (lastSelectedItem.Type)
				{
					case ClassIDType.Texture2D:
					case ClassIDType.Sprite:
					{
						if (enablePreview.Checked && imageTexture != null)
						{
							previewPanel.BackgroundImage = imageTexture.Bitmap;
						}
						else
						{
							previewPanel.BackgroundImage = Properties.Resources.preview;
							previewPanel.BackgroundImageLayout = ImageLayout.Center;
						}
					}
					break;
					case ClassIDType.Shader:
					case ClassIDType.TextAsset:
					case ClassIDType.MonoBehaviour:
						textPreviewBox.Visible = !textPreviewBox.Visible;
						break;
					case ClassIDType.Font:
						fontPreviewBox.Visible = !fontPreviewBox.Visible;
						break;
					case ClassIDType.AudioClip:
					{
						FMODpanel.Visible = !FMODpanel.Visible;

						if (sound != null && channel != null)
						{
							var result = channel.isPlaying(out var playing);
							if (result == FMOD.RESULT.OK && playing)
							{
								channel.stop();
								FMODreset();
							}
						}
						else if (FMODpanel.Visible)
						{
							PreviewAsset(lastSelectedItem);
						}

						break;
					}

				}

			}
			else if (lastSelectedItem != null && enablePreview.Checked)
			{
				PreviewAsset(lastSelectedItem);
			}

			Properties.Settings.Default.enablePreview = enablePreview.Checked;
			Properties.Settings.Default.Save();
		}

		private void displayAssetInfo_Check(object sender, EventArgs e)
		{
			if (displayInfo.Checked && assetInfoLabel.Text != null)
			{
				assetInfoLabel.Visible = true;
			}
			else
			{
				assetInfoLabel.Visible = false;
			}

			Properties.Settings.Default.displayInfo = displayInfo.Checked;
			Properties.Settings.Default.Save();
		}

		private void showExpOpt_Click(object sender, EventArgs e)
		{
			var exportOpt = new ExportOptions();
			exportOpt.ShowDialog(this);
		}

		private void assetListView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
		{
			e.Item = visibleAssets[e.ItemIndex];
		}

		private void tabPageSelected(object sender, TabControlEventArgs e)
		{
			switch (e.TabPageIndex)
			{
				case 0:
					treeSearch.Select();
					break;
				case 1:
					listSearch.Select();
					break;
			}
		}

		private void treeSearch_Enter(object sender, EventArgs e)
		{
			if (treeSearch.Text == " Search ")
			{
				treeSearch.Text = "";
				treeSearch.ForeColor = SystemColors.WindowText;
			}
		}

		private void treeSearch_Leave(object sender, EventArgs e)
		{
			if (treeSearch.Text == "")
			{
				treeSearch.Text = " Search ";
				treeSearch.ForeColor = SystemColors.GrayText;
			}
		}

		private void treeSearch_TextChanged(object sender, EventArgs e)
		{
			treeSrcResults.Clear();
			nextGObject = 0;
		}

		private void treeSearch_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Enter)
			{
				if (treeSrcResults.Count == 0)
				{
					foreach (TreeNode node in sceneTreeView.Nodes)
					{
						TreeNodeSearch(node);
					}
				}
				if (treeSrcResults.Count > 0)
				{
					if (nextGObject >= treeSrcResults.Count)
					{
						nextGObject = 0;
					}
					treeSrcResults[nextGObject].EnsureVisible();
					sceneTreeView.SelectedNode = treeSrcResults[nextGObject];
					nextGObject++;
				}
			}
		}

		private void TreeNodeSearch(TreeNode treeNode)
		{
			if (treeNode.Text.IndexOf(treeSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				treeSrcResults.Add(treeNode);
			}

			foreach (TreeNode node in treeNode.Nodes)
			{
				TreeNodeSearch(node);
			}
		}

		private void sceneTreeView_AfterCheck(object sender, TreeViewEventArgs e)
		{
			foreach (TreeNode childNode in e.Node.Nodes)
			{
				childNode.Checked = e.Node.Checked;
			}
		}

		private void listSearch_Enter(object sender, EventArgs e)
		{
			if (listSearch.Text == " Filter ")
			{
				listSearch.Text = "";
				listSearch.ForeColor = SystemColors.WindowText;
				enableFiltering = true;
			}
		}

		private void listSearch_Leave(object sender, EventArgs e)
		{
			if (listSearch.Text == "")
			{
				enableFiltering = false;
				listSearch.Text = " Filter ";
				listSearch.ForeColor = SystemColors.GrayText;
			}
		}

		private void ListSearchTextChanged(object sender, EventArgs e)
		{
			if (enableFiltering)
			{
				if (delayTimer.Enabled)
				{
					delayTimer.Stop();
					delayTimer.Start();
				}
				else
				{
					delayTimer.Start();
				}
			}
		}

		private void delayTimer_Elapsed(object sender, ElapsedEventArgs e)
		{
			delayTimer.Stop();
			Invoke(new Action(FilterAssetList));
		}

		private void assetListView_ColumnClick(object sender, ColumnClickEventArgs e)
		{
			if (sortColumn != e.Column)
			{
				reverseSort = false;
			}
			else
			{
				reverseSort = !reverseSort;
			}
			sortColumn = e.Column;
			assetListView.BeginUpdate();
			assetListView.SelectedIndices.Clear();
			if (sortColumn == 4) //FullSize
			{
				visibleAssets.Sort((a, b) =>
				{
					var asf = a.FullSize;
					var bsf = b.FullSize;
					return reverseSort ? bsf.CompareTo(asf) : asf.CompareTo(bsf);
				});
			}
			else if (sortColumn == 3) // PathID
			{
				visibleAssets.Sort((x, y) =>
				{
					long pathID_X = x.m_PathID;
					long pathID_Y = y.m_PathID;
					return reverseSort ? pathID_Y.CompareTo(pathID_X) : pathID_X.CompareTo(pathID_Y);
				});
			}
			else
			{
				visibleAssets.Sort((a, b) =>
				{
					var at = a.SubItems[sortColumn].Text;
					var bt = b.SubItems[sortColumn].Text;
					return reverseSort ? bt.CompareTo(at) : at.CompareTo(bt);
				});
			}
			assetListView.EndUpdate();
		}

		private void selectAsset(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			previewPanel.BackgroundImage = Properties.Resources.preview;
			previewPanel.BackgroundImageLayout = ImageLayout.Center;
			classTextBox.Visible = false;
			assetInfoLabel.Visible = false;
			assetInfoLabel.Text = null;
			textPreviewBox.Visible = false;
			fontPreviewBox.Visible = false;
			FMODpanel.Visible = false;
			glControl1.Visible = false;
			StatusStripUpdate("");

			FMODreset();

			lastSelectedItem = (AssetItem)e.Item;

			if (e.IsSelected)
			{
				if (tabControl2.SelectedIndex == 1)
				{
					dumpTextBox.Text = DumpAsset(lastSelectedItem.Asset);
				}
				if (enablePreview.Checked)
				{
					PreviewAsset(lastSelectedItem);
					if (displayInfo.Checked && lastSelectedItem.InfoText != null)
					{
						assetInfoLabel.Text = lastSelectedItem.InfoText;
						assetInfoLabel.Visible = true;
					}
				}
			}
		}

		private void classesListView_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			classTextBox.Visible = true;
			assetInfoLabel.Visible = false;
			assetInfoLabel.Text = null;
			textPreviewBox.Visible = false;
			fontPreviewBox.Visible = false;
			FMODpanel.Visible = false;
			glControl1.Visible = false;
			StatusStripUpdate("");
			if (e.IsSelected)
			{
				classTextBox.Text = ((TypeTreeItem)classesListView.SelectedItems[0]).ToString();
			}
		}

		private void preview_Resize(object sender, EventArgs e)
		{
			if (glControlLoaded && glControl1.Visible)
			{
				ChangeGLSize(glControl1.Size);
				glControl1.Invalidate();
			}
		}

		private void PreviewAsset(AssetItem assetItem)
		{
			if (assetItem == null)
				return;
			try
			{
				switch (assetItem.Asset)
				{
					case Texture2D m_Texture2D:
						PreviewTexture2D(assetItem, m_Texture2D);
						break;
					case AudioClip m_AudioClip:
						PreviewAudioClip(assetItem, m_AudioClip);
						break;
					case Shader m_Shader:
						PreviewShader(m_Shader);
						break;
					case TextAsset m_TextAsset:
						PreviewTextAsset(m_TextAsset);
						break;
					case MonoBehaviour m_MonoBehaviour:
						PreviewMonoBehaviour(m_MonoBehaviour);
						break;
					case Font m_Font:
						PreviewFont(m_Font);
						break;
					case Mesh m_Mesh:
						PreviewMesh(m_Mesh);
						break;
					case VideoClip _:
					case MovieTexture _:
						StatusStripUpdate("Only supported export.");
						break;
					case Sprite m_Sprite:
						PreviewSprite(assetItem, m_Sprite);
						break;
					case Animator m_Animator:
						PreviewAnimator(m_Animator);
						StatusStripUpdate("Can be exported to FBX file.");
						break;
					case AnimationClip _:
						StatusStripUpdate("Can be exported with Animator or Objects");
						break;
					default:
						var str = assetItem.Asset.Dump();
						if (str != null)
						{
							textPreviewBox.Text = str;
							textPreviewBox.Visible = true;
						}
						break;
				}
			}
			catch (Exception e)
			{
				MessageBox.Show($"Preview {assetItem.Type}:{assetItem.Text} error\r\n{e.Message}\r\n{e.StackTrace}");
			}
		}

		private void PreviewTexture2D(AssetItem assetItem, Texture2D m_Texture2D)
		{
			var image = m_Texture2D.ConvertToImage(true);
			if (image != null)
			{
				var bitmap = new DirectBitmap(image.ConvertToBytes(), m_Texture2D.m_Width, m_Texture2D.m_Height);
				image.Dispose();
				assetItem.InfoText = $"Width: {m_Texture2D.m_Width}\nHeight: {m_Texture2D.m_Height}\nFormat: {m_Texture2D.m_TextureFormat}";
				switch (m_Texture2D.m_TextureSettings.m_FilterMode)
				{
					case 0: assetItem.InfoText += "\nFilter Mode: Point "; break;
					case 1: assetItem.InfoText += "\nFilter Mode: Bilinear "; break;
					case 2: assetItem.InfoText += "\nFilter Mode: Trilinear "; break;
				}
				assetItem.InfoText += $"\nAnisotropic level: {m_Texture2D.m_TextureSettings.m_Aniso}\nMip map bias: {m_Texture2D.m_TextureSettings.m_MipBias}";
				switch (m_Texture2D.m_TextureSettings.m_WrapMode)
				{
					case 0: assetItem.InfoText += "\nWrap mode: Repeat"; break;
					case 1: assetItem.InfoText += "\nWrap mode: Clamp"; break;
				}
				assetItem.InfoText += "\nChannels: ";
				int validChannel = 0;
				for (int i = 0; i < 4; i++)
				{
					if (textureChannels[i])
					{
						assetItem.InfoText += textureChannelNames[i];
						validChannel++;
					}
				}
				if (validChannel == 0)
					assetItem.InfoText += "None";
				if (validChannel != 4)
				{
					var bytes = bitmap.Bits;
					for (int i = 0; i < bitmap.Height; i++)
					{
						int offset = Math.Abs(bitmap.Stride) * i;
						for (int j = 0; j < bitmap.Width; j++)
						{
							bytes[offset] = textureChannels[0] ? bytes[offset] : validChannel == 1 && textureChannels[3] ? byte.MaxValue : byte.MinValue;
							bytes[offset + 1] = textureChannels[1] ? bytes[offset + 1] : validChannel == 1 && textureChannels[3] ? byte.MaxValue : byte.MinValue;
							bytes[offset + 2] = textureChannels[2] ? bytes[offset + 2] : validChannel == 1 && textureChannels[3] ? byte.MaxValue : byte.MinValue;
							bytes[offset + 3] = textureChannels[3] ? bytes[offset + 3] : byte.MaxValue;
							offset += 4;
						}
					}
				}
				PreviewTexture(bitmap);

				StatusStripUpdate("'Ctrl'+'R'/'G'/'B'/'A' for Channel Toggle");
			}
			else
			{
				StatusStripUpdate("Unsupported image for preview");
			}
		}

		private void PreviewAudioClip(AssetItem assetItem, AudioClip m_AudioClip)
		{
			//Info
			assetItem.InfoText = "Compression format: ";
			if (m_AudioClip.version[0] < 5)
			{
				switch (m_AudioClip.m_Type)
				{
					case FMODSoundType.ACC:
						assetItem.InfoText += "Acc";
						break;
					case FMODSoundType.AIFF:
						assetItem.InfoText += "AIFF";
						break;
					case FMODSoundType.IT:
						assetItem.InfoText += "Impulse tracker";
						break;
					case FMODSoundType.MOD:
						assetItem.InfoText += "Protracker / Fasttracker MOD";
						break;
					case FMODSoundType.MPEG:
						assetItem.InfoText += "MP2/MP3 MPEG";
						break;
					case FMODSoundType.OGGVORBIS:
						assetItem.InfoText += "Ogg vorbis";
						break;
					case FMODSoundType.S3M:
						assetItem.InfoText += "ScreamTracker 3";
						break;
					case FMODSoundType.WAV:
						assetItem.InfoText += "Microsoft WAV";
						break;
					case FMODSoundType.XM:
						assetItem.InfoText += "FastTracker 2 XM";
						break;
					case FMODSoundType.XMA:
						assetItem.InfoText += "Xbox360 XMA";
						break;
					case FMODSoundType.VAG:
						assetItem.InfoText += "PlayStation Portable ADPCM";
						break;
					case FMODSoundType.AUDIOQUEUE:
						assetItem.InfoText += "iPhone";
						break;
					default:
						assetItem.InfoText += "Unknown";
						break;
				}
			}
			else
			{
				switch (m_AudioClip.m_CompressionFormat)
				{
					case AudioCompressionFormat.PCM:
						assetItem.InfoText += "PCM";
						break;
					case AudioCompressionFormat.Vorbis:
						assetItem.InfoText += "Vorbis";
						break;
					case AudioCompressionFormat.ADPCM:
						assetItem.InfoText += "ADPCM";
						break;
					case AudioCompressionFormat.MP3:
						assetItem.InfoText += "MP3";
						break;
					case AudioCompressionFormat.PSMVAG:
						assetItem.InfoText += "PlayStation Portable ADPCM";
						break;
					case AudioCompressionFormat.HEVAG:
						assetItem.InfoText += "PSVita ADPCM";
						break;
					case AudioCompressionFormat.XMA:
						assetItem.InfoText += "Xbox360 XMA";
						break;
					case AudioCompressionFormat.AAC:
						assetItem.InfoText += "AAC";
						break;
					case AudioCompressionFormat.GCADPCM:
						assetItem.InfoText += "Nintendo 3DS/Wii DSP";
						break;
					case AudioCompressionFormat.ATRAC9:
						assetItem.InfoText += "PSVita ATRAC9";
						break;
					default:
						assetItem.InfoText += "Unknown";
						break;
				}
			}

			var m_AudioData = m_AudioClip.m_AudioData.GetData();
			if (m_AudioData == null || m_AudioData.Length == 0)
				return;
			var exinfo = new FMOD.CREATESOUNDEXINFO();

			exinfo.cbsize = Marshal.SizeOf(exinfo);
			exinfo.length = (uint)m_AudioClip.m_Size;

			var result = system.createSound(m_AudioData, FMOD.MODE.OPENMEMORY | loopMode, ref exinfo, out sound);
			if (ERRCHECK(result)) return;

			sound.getNumSubSounds(out var numsubsounds);

			if (numsubsounds > 0)
			{
				result = sound.getSubSound(0, out var subsound);
				if (result == FMOD.RESULT.OK)
				{
					sound = subsound;
				}
			}

			result = sound.getLength(out FMODlenms, FMOD.TIMEUNIT.MS);
			if (ERRCHECK(result)) return;

			result = system.playSound(sound, null, true, out channel);
			if (ERRCHECK(result)) return;

			FMODpanel.Visible = true;

			result = channel.getFrequency(out var frequency);
			if (ERRCHECK(result)) return;

			FMODinfoLabel.Text = frequency + " Hz";
			FMODtimerLabel.Text = $"0:0.0 / {FMODlenms / 1000 / 60}:{FMODlenms / 1000 % 60}.{FMODlenms / 10 % 100}";
		}

		private void PreviewShader(Shader m_Shader)
		{
			var str = ShaderConverter.Convert(m_Shader);
			PreviewText(str == null ? "Serialized Shader can't be read" : str.Replace("\n", "\r\n"));
		}

		private void PreviewTextAsset(TextAsset m_TextAsset)
		{
			var text = Encoding.UTF8.GetString(m_TextAsset.m_Script);
			text = text.Replace("\n", "\r\n").Replace("\0", "");
			PreviewText(text);
		}

		private void PreviewMonoBehaviour(MonoBehaviour m_MonoBehaviour)
		{
			var obj = m_MonoBehaviour.ToType();
			if (obj == null)
			{
				var type = MonoBehaviourToTypeTree(m_MonoBehaviour);
				obj = m_MonoBehaviour.ToType(type);
			}
			var str = JsonConvert.SerializeObject(obj, Formatting.Indented);
			PreviewText(str);
		}

		private void PreviewFont(Font m_Font)
		{
			if (m_Font.m_FontData != null)
			{
				var data = Marshal.AllocCoTaskMem(m_Font.m_FontData.Length);
				Marshal.Copy(m_Font.m_FontData, 0, data, m_Font.m_FontData.Length);

				uint cFonts = 0;
				var re = AddFontMemResourceEx(data, (uint)m_Font.m_FontData.Length, IntPtr.Zero, ref cFonts);
				if (re != IntPtr.Zero)
				{
					using (var pfc = new PrivateFontCollection())
					{
						pfc.AddMemoryFont(data, m_Font.m_FontData.Length);
						Marshal.FreeCoTaskMem(data);
						if (pfc.Families.Length > 0)
						{
							fontPreviewBox.SelectionStart = 0;
							fontPreviewBox.SelectionLength = 80;
							fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 16, FontStyle.Regular);
							fontPreviewBox.SelectionStart = 81;
							fontPreviewBox.SelectionLength = 56;
							fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 12, FontStyle.Regular);
							fontPreviewBox.SelectionStart = 138;
							fontPreviewBox.SelectionLength = 56;
							fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 18, FontStyle.Regular);
							fontPreviewBox.SelectionStart = 195;
							fontPreviewBox.SelectionLength = 56;
							fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 24, FontStyle.Regular);
							fontPreviewBox.SelectionStart = 252;
							fontPreviewBox.SelectionLength = 56;
							fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 36, FontStyle.Regular);
							fontPreviewBox.SelectionStart = 309;
							fontPreviewBox.SelectionLength = 56;
							fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 48, FontStyle.Regular);
							fontPreviewBox.SelectionStart = 366;
							fontPreviewBox.SelectionLength = 56;
							fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 60, FontStyle.Regular);
							fontPreviewBox.SelectionStart = 423;
							fontPreviewBox.SelectionLength = 55;
							fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 72, FontStyle.Regular);
							fontPreviewBox.Visible = true;
						}
					}
					return;
				}
			}
			StatusStripUpdate("Unsupported font for preview. Try to export.");
		}

		private void PreviewMesh(Mesh m_Mesh)
		{
			glControl1.Visible = true;
			if (!renderer.SetModel(m_Mesh))
			{
				glControl1.Visible = false;
				StatusStripUpdate("Unable to preview this mesh");
			} else
			{
				StatusStripUpdate("Using OpenGL Version: " + GL.GetString(StringName.Version) + "\n"
									  + "'Mouse Left'=Rotate | 'Mouse Right'=Move | 'Mouse Wheel'=Zoom \n"
									  + "'Ctrl W'=Wireframe | 'Ctrl S'=Shade | 'Ctrl N'=ReNormal ");
			}
		}

		private void PreviewSprite(AssetItem assetItem, Sprite m_Sprite)
		{
			var image = m_Sprite.GetImage();
			if (image != null)
			{
				var bitmap = new DirectBitmap(image.ConvertToBytes(), image.Width, image.Height);
				image.Dispose();
				assetItem.InfoText = $"Width: {bitmap.Width}\nHeight: {bitmap.Height}\n";
				PreviewTexture(bitmap);
			}
			else
			{
				StatusStripUpdate("Unsupported sprite for preview.");
			}
		}

		private void PreviewTexture(DirectBitmap bitmap)
		{
			imageTexture?.Dispose();
			imageTexture = bitmap;
			previewPanel.BackgroundImage = imageTexture.Bitmap;
			if (imageTexture.Width > previewPanel.Width || imageTexture.Height > previewPanel.Height)
				previewPanel.BackgroundImageLayout = ImageLayout.Zoom;
			else
				previewPanel.BackgroundImageLayout = ImageLayout.Center;
		}

		private void PreviewText(string text)
		{
			textPreviewBox.Text = text;
			textPreviewBox.Visible = true;
		}

		private void SetProgressBarValue(int value)
		{
			if (InvokeRequired)
			{
				BeginInvoke(new Action(() => { progressBar1.Value = value; }));
			}
			else
			{
				progressBar1.Value = value;
			}
		}

		private void StatusStripUpdate(string statusText)
		{
			if (InvokeRequired)
			{
				BeginInvoke(new Action(() => { toolStripStatusLabel1.Text = statusText; }));
			}
			else
			{
				toolStripStatusLabel1.Text = statusText;
			}
		}

		private void ResetForm()
		{
			Text = $"AssetStudioGUI v{Application.ProductVersion}";
			assetsManager.Clear();
			assemblyLoader.Clear();
			exportableAssets.Clear();
			visibleAssets.Clear();
			sceneTreeView.Nodes.Clear();
			assetListView.VirtualListSize = 0;
			assetListView.Items.Clear();
			classesListView.Items.Clear();
			classesListView.Groups.Clear();
			previewPanel.BackgroundImage = Properties.Resources.preview;
			imageTexture?.Dispose();
			imageTexture = null;
			previewPanel.BackgroundImageLayout = ImageLayout.Center;
			assetInfoLabel.Visible = false;
			assetInfoLabel.Text = null;
			textPreviewBox.Visible = false;
			fontPreviewBox.Visible = false;
			glControl1.Visible = false;
			lastSelectedItem = null;
			sortColumn = -1;
			reverseSort = false;
			enableFiltering = false;
			listSearch.Text = " Filter ";

			var count = filterTypeToolStripMenuItem.DropDownItems.Count;
			for (var i = 1; i < count; i++)
			{
				filterTypeToolStripMenuItem.DropDownItems.RemoveAt(1);
			}

			FMODreset();
		}

		private void assetListView_MouseClick(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Right && assetListView.SelectedIndices.Count > 0)
			{
				goToSceneHierarchyToolStripMenuItem.Visible = false;
				showOriginalFileToolStripMenuItem.Visible = false;
				exportAnimatorwithselectedAnimationClipMenuItem.Visible = false;

				if (assetListView.SelectedIndices.Count == 1)
				{
					goToSceneHierarchyToolStripMenuItem.Visible = true;
					showOriginalFileToolStripMenuItem.Visible = true;
				}
				if (assetListView.SelectedIndices.Count >= 1)
				{
					var selectedAssets = GetSelectedAssets();
					if (selectedAssets.Any(x => x.Type == ClassIDType.Animator) && selectedAssets.Any(x => x.Type == ClassIDType.AnimationClip))
					{
						exportAnimatorwithselectedAnimationClipMenuItem.Visible = true;
					}
				}

				tempClipboard = assetListView.HitTest(new Point(e.X, e.Y)).SubItem.Text;
				contextMenuStrip1.Show(assetListView, e.X, e.Y);
			}
		}

		private void copyToolStripMenuItem_Click(object sender, EventArgs e)
		{
			Clipboard.SetDataObject(tempClipboard);
		}

		private void exportSelectedAssetsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			ExportAssets(ExportFilter.Selected, ExportType.Convert);
		}

		private void showOriginalFileToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var selectasset = (AssetItem)assetListView.Items[assetListView.SelectedIndices[0]];
			var args = $"/select, \"{selectasset.SourceFile.originalPath ?? selectasset.SourceFile.fullName}\"";
			var pfi = new ProcessStartInfo("explorer.exe", args);
			Process.Start(pfi);
		}

		private void exportAnimatorwithAnimationClipMenuItem_Click(object sender, EventArgs e)
		{
			AssetItem animator = null;
			List<AssetItem> animationList = new List<AssetItem>();
			var selectedAssets = GetSelectedAssets();
			foreach (var assetPreloadData in selectedAssets)
			{
				if (assetPreloadData.Type == ClassIDType.Animator)
				{
					animator = assetPreloadData;
				}
				else if (assetPreloadData.Type == ClassIDType.AnimationClip)
				{
					animationList.Add(assetPreloadData);
				}
			}

			if (animator != null)
			{
				var saveFolderDialog = new OpenFolderDialog();
				saveFolderDialog.InitialFolder = saveDirectoryBackup;
				if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
				{
					saveDirectoryBackup = saveFolderDialog.Folder;
					var exportPath = Path.Combine(saveFolderDialog.Folder, "Animator") + Path.DirectorySeparatorChar;
					ExportAnimatorWithAnimationClip(animator, animationList, exportPath);
				}
			}
		}

		private void exportSelectedObjectsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			ExportObjects(false);
		}

		private void exportObjectswithAnimationClipMenuItem_Click(object sender, EventArgs e)
		{
			ExportObjects(true);
		}

		private void ExportObjects(bool animation)
		{
			if (sceneTreeView.Nodes.Count > 0)
			{
				var saveFolderDialog = new OpenFolderDialog();
				saveFolderDialog.InitialFolder = saveDirectoryBackup;
				if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
				{
					saveDirectoryBackup = saveFolderDialog.Folder;
					var exportPath = Path.Combine(saveFolderDialog.Folder, "GameObject") + Path.DirectorySeparatorChar;
					List<AssetItem> animationList = null;
					if (animation)
					{
						animationList = GetSelectedAssets().Where(x => x.Type == ClassIDType.AnimationClip).ToList();
						if (animationList.Count == 0)
						{
							animationList = null;
						}
					}
					ExportObjectsWithAnimationClip(exportPath, sceneTreeView.Nodes, animationList);
				}
			}
			else
			{
				StatusStripUpdate("No Objects available for export");
			}
		}

		private void exportSelectedObjectsmergeToolStripMenuItem_Click(object sender, EventArgs e)
		{
			ExportMergeObjects(false);
		}

		private void exportSelectedObjectsmergeWithAnimationClipToolStripMenuItem_Click(object sender, EventArgs e)
		{
			ExportMergeObjects(true);
		}

		private void ExportMergeObjects(bool animation)
		{
			if (sceneTreeView.Nodes.Count > 0)
			{
				var gameObjects = new List<GameObject>();
				GetSelectedParentNode(sceneTreeView.Nodes, gameObjects);
				if (gameObjects.Count > 0)
				{
					var saveFileDialog = new SaveFileDialog();
					saveFileDialog.FileName = gameObjects[0].m_Name + " (merge).fbx";
					saveFileDialog.AddExtension = false;
					saveFileDialog.Filter = "Fbx file (*.fbx)|*.fbx";
					saveFileDialog.InitialDirectory = saveDirectoryBackup;
					if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
					{
						saveDirectoryBackup = Path.GetDirectoryName(saveFileDialog.FileName);
						var exportPath = saveFileDialog.FileName;
						List<AssetItem> animationList = null;
						if (animation)
						{
							animationList = GetSelectedAssets().Where(x => x.Type == ClassIDType.AnimationClip).ToList();
							if (animationList.Count == 0)
							{
								animationList = null;
							}
						}
						ExportObjectsMergeWithAnimationClip(exportPath, gameObjects, animationList);
					}
				}
				else
				{
					StatusStripUpdate("No Object selected for export.");
				}
			}
		}

		private void goToSceneHierarchyToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var selectasset = (AssetItem)assetListView.Items[assetListView.SelectedIndices[0]];
			if (selectasset.TreeNode != null)
			{
				sceneTreeView.SelectedNode = selectasset.TreeNode;
				tabControl1.SelectedTab = tabPage1;
			}
		}

		private void exportAllAssetsMenuItem_Click(object sender, EventArgs e)
		{
			ExportAssets(ExportFilter.All, ExportType.Convert);
		}

		private void exportSelectedAssetsMenuItem_Click(object sender, EventArgs e)
		{
			ExportAssets(ExportFilter.Selected, ExportType.Convert);
		}

		private void exportFilteredAssetsMenuItem_Click(object sender, EventArgs e)
		{
			ExportAssets(ExportFilter.Filtered, ExportType.Convert);
		}

		private void toolStripMenuItem4_Click(object sender, EventArgs e)
		{
			ExportAssets(ExportFilter.All, ExportType.Raw);
		}

		private void toolStripMenuItem5_Click(object sender, EventArgs e)
		{
			ExportAssets(ExportFilter.Selected, ExportType.Raw);
		}

		private void toolStripMenuItem6_Click(object sender, EventArgs e)
		{
			ExportAssets(ExportFilter.Filtered, ExportType.Raw);
		}

		private void toolStripMenuItem7_Click(object sender, EventArgs e)
		{
			ExportAssets(ExportFilter.All, ExportType.Dump);
		}

		private void toolStripMenuItem8_Click(object sender, EventArgs e)
		{
			ExportAssets(ExportFilter.Selected, ExportType.Dump);
		}

		private void toolStripMenuItem9_Click(object sender, EventArgs e)
		{
			ExportAssets(ExportFilter.Filtered, ExportType.Dump);
		}

		private void toolStripMenuItem11_Click(object sender, EventArgs e)
		{
			ExportAssetsList(ExportFilter.All);
		}

		private void toolStripMenuItem12_Click(object sender, EventArgs e)
		{
			ExportAssetsList(ExportFilter.Selected);
		}

		private void toolStripMenuItem13_Click(object sender, EventArgs e)
		{
			ExportAssetsList(ExportFilter.Filtered);
		}

		private void exportAllObjectssplitToolStripMenuItem1_Click(object sender, EventArgs e)
		{
			if (sceneTreeView.Nodes.Count > 0)
			{
				var saveFolderDialog = new OpenFolderDialog();
				saveFolderDialog.InitialFolder = saveDirectoryBackup;
				if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
				{
					saveDirectoryBackup = saveFolderDialog.Folder;
					var savePath = saveFolderDialog.Folder + Path.DirectorySeparatorChar;
					ExportSplitObjects(savePath, sceneTreeView.Nodes);
				}
			}
			else
			{
				StatusStripUpdate("No Objects available for export");
			}
		}

		private List<AssetItem> GetSelectedAssets()
		{
			var selectedAssets = new List<AssetItem>(assetListView.SelectedIndices.Count);
			foreach (int index in assetListView.SelectedIndices)
			{
				selectedAssets.Add((AssetItem)assetListView.Items[index]);
			}

			return selectedAssets;
		}

		private void FilterAssetList()
		{
			assetListView.BeginUpdate();
			assetListView.SelectedIndices.Clear();
			var show = new List<ClassIDType>();
			if (!allToolStripMenuItem.Checked)
			{
				for (var i = 1; i < filterTypeToolStripMenuItem.DropDownItems.Count; i++)
				{
					var item = (ToolStripMenuItem)filterTypeToolStripMenuItem.DropDownItems[i];
					if (item.Checked)
					{
						show.Add((ClassIDType)Enum.Parse(typeof(ClassIDType), item.Text));
					}
				}
				visibleAssets = exportableAssets.FindAll(x => show.Contains(x.Type));
			}
			else
			{
				visibleAssets = exportableAssets;
			}
			if (listSearch.Text != " Filter ")
			{
				visibleAssets = visibleAssets.FindAll(
					x => x.Text.IndexOf(listSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0 ||
					x.SubItems[1].Text.IndexOf(listSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0 ||
					x.SubItems[3].Text.IndexOf(listSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0);
			}
			assetListView.VirtualListSize = visibleAssets.Count;
			assetListView.EndUpdate();
		}

		private void ExportAssets(ExportFilter type, ExportType exportType)
		{
			if (exportableAssets.Count > 0)
			{
				var saveFolderDialog = new OpenFolderDialog();
				saveFolderDialog.InitialFolder = saveDirectoryBackup;
				if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
				{
					timer.Stop();
					saveDirectoryBackup = saveFolderDialog.Folder;
					List<AssetItem> toExportAssets = null;
					switch (type)
					{
						case ExportFilter.All:
							toExportAssets = exportableAssets;
							break;
						case ExportFilter.Selected:
							toExportAssets = GetSelectedAssets();
							break;
						case ExportFilter.Filtered:
							toExportAssets = visibleAssets;
							break;
					}
					Studio.ExportAssets(saveFolderDialog.Folder, toExportAssets, exportType);
				}
			}
			else
			{
				StatusStripUpdate("No exportable assets loaded");
			}
		}

		private void ExportAssetsList(ExportFilter type)
		{
			// XXX: Only exporting as XML for now, but would JSON(/CSV/other) be useful too?

			if (exportableAssets.Count > 0)
			{
				var saveFolderDialog = new OpenFolderDialog();
				saveFolderDialog.InitialFolder = saveDirectoryBackup;
				if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
				{
					timer.Stop();
					saveDirectoryBackup = saveFolderDialog.Folder;
					List<AssetItem> toExportAssets = null;
					switch (type)
					{
						case ExportFilter.All:
							toExportAssets = exportableAssets;
							break;
						case ExportFilter.Selected:
							toExportAssets = GetSelectedAssets();
							break;
						case ExportFilter.Filtered:
							toExportAssets = visibleAssets;
							break;
					}
					Studio.ExportAssetsList(saveFolderDialog.Folder, toExportAssets, ExportListType.XML);
				}
			}
			else
			{
				StatusStripUpdate("No exportable assets loaded");
			}
		}

		#region FMOD
		private void FMODinit()
		{
			FMODreset();

			var result = FMOD.Factory.System_Create(out system);
			if (ERRCHECK(result)) { return; }

			result = system.getVersion(out var version);
			ERRCHECK(result);
			if (version < FMOD.VERSION.number)
			{
				MessageBox.Show($"Error!  You are using an old version of FMOD {version:X}.  This program requires {FMOD.VERSION.number:X}.");
				Application.Exit();
			}

			result = system.init(2, FMOD.INITFLAGS.NORMAL, IntPtr.Zero);
			if (ERRCHECK(result)) { return; }

			result = system.getMasterSoundGroup(out masterSoundGroup);
			if (ERRCHECK(result)) { return; }

			result = masterSoundGroup.setVolume(FMODVolume);
			if (ERRCHECK(result)) { return; }
		}

		private void FMODreset()
		{
			timer.Stop();
			FMODprogressBar.Value = 0;
			FMODtimerLabel.Text = "0:00.0 / 0:00.0";
			FMODstatusLabel.Text = "Stopped";
			FMODinfoLabel.Text = "";

			if (sound != null && sound.isValid())
			{
				var result = sound.release();
				ERRCHECK(result);
				sound = null;
			}
		}

		private void FMODplayButton_Click(object sender, EventArgs e)
		{
			if (sound != null && channel != null)
			{
				timer.Start();
				var result = channel.isPlaying(out var playing);
				if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
				{
					if (ERRCHECK(result)) { return; }
				}

				if (playing)
				{
					result = channel.stop();
					if (ERRCHECK(result)) { return; }

					result = system.playSound(sound, null, false, out channel);
					if (ERRCHECK(result)) { return; }

					FMODpauseButton.Text = "Pause";
				}
				else
				{
					result = system.playSound(sound, null, false, out channel);
					if (ERRCHECK(result)) { return; }
					FMODstatusLabel.Text = "Playing";

					if (FMODprogressBar.Value > 0)
					{
						uint newms = FMODlenms / 1000 * (uint)FMODprogressBar.Value;

						result = channel.setPosition(newms, FMOD.TIMEUNIT.MS);
						if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
						{
							if (ERRCHECK(result)) { return; }
						}

					}
				}
			}
		}

		private void FMODpauseButton_Click(object sender, EventArgs e)
		{
			if (sound != null && channel != null)
			{
				var result = channel.isPlaying(out var playing);
				if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
				{
					if (ERRCHECK(result)) { return; }
				}

				if (playing)
				{
					result = channel.getPaused(out var paused);
					if (ERRCHECK(result)) { return; }
					result = channel.setPaused(!paused);
					if (ERRCHECK(result)) { return; }

					if (paused)
					{
						FMODstatusLabel.Text = "Playing";
						FMODpauseButton.Text = "Pause";
						timer.Start();
					}
					else
					{
						FMODstatusLabel.Text = "Paused";
						FMODpauseButton.Text = "Resume";
						timer.Stop();
					}
				}
			}
		}

		private void FMODstopButton_Click(object sender, EventArgs e)
		{
			if (channel != null)
			{
				var result = channel.isPlaying(out var playing);
				if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
				{
					if (ERRCHECK(result)) { return; }
				}

				if (playing)
				{
					result = channel.stop();
					if (ERRCHECK(result)) { return; }
					//channel = null;
					//don't FMODreset, it will nullify the sound
					timer.Stop();
					FMODprogressBar.Value = 0;
					FMODtimerLabel.Text = "0:00.0 / 0:00.0";
					FMODstatusLabel.Text = "Stopped";
					FMODpauseButton.Text = "Pause";
				}
			}
		}

		private void FMODloopButton_CheckedChanged(object sender, EventArgs e)
		{
			FMOD.RESULT result;

			loopMode = FMODloopButton.Checked ? FMOD.MODE.LOOP_NORMAL : FMOD.MODE.LOOP_OFF;

			if (sound != null)
			{
				result = sound.setMode(loopMode);
				if (ERRCHECK(result)) { return; }
			}

			if (channel != null)
			{
				result = channel.isPlaying(out var playing);
				if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
				{
					if (ERRCHECK(result)) { return; }
				}

				result = channel.getPaused(out var paused);
				if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
				{
					if (ERRCHECK(result)) { return; }
				}

				if (playing || paused)
				{
					result = channel.setMode(loopMode);
					if (ERRCHECK(result)) { return; }
				}
			}
		}

		private void FMODvolumeBar_ValueChanged(object sender, EventArgs e)
		{
			FMODVolume = Convert.ToSingle(FMODvolumeBar.Value) / 10;

			var result = masterSoundGroup.setVolume(FMODVolume);
			if (ERRCHECK(result)) { return; }
		}

		private void FMODprogressBar_Scroll(object sender, EventArgs e)
		{
			if (channel != null)
			{
				uint newms = FMODlenms / 1000 * (uint)FMODprogressBar.Value;
				FMODtimerLabel.Text = $"{newms / 1000 / 60}:{newms / 1000 % 60}.{newms / 10 % 100}/{FMODlenms / 1000 / 60}:{FMODlenms / 1000 % 60}.{FMODlenms / 10 % 100}";
			}
		}

		private void FMODprogressBar_MouseDown(object sender, MouseEventArgs e)
		{
			timer.Stop();
		}

		private void FMODprogressBar_MouseUp(object sender, MouseEventArgs e)
		{
			if (channel != null)
			{
				uint newms = FMODlenms / 1000 * (uint)FMODprogressBar.Value;

				var result = channel.setPosition(newms, FMOD.TIMEUNIT.MS);
				if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
				{
					if (ERRCHECK(result)) { return; }
				}


				result = channel.isPlaying(out var playing);
				if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
				{
					if (ERRCHECK(result)) { return; }
				}

				if (playing) { timer.Start(); }
			}
		}

		private void timer_Tick(object sender, EventArgs e)
		{
			uint ms = 0;
			bool playing = false;
			bool paused = false;

			if (channel != null)
			{
				var result = channel.getPosition(out ms, FMOD.TIMEUNIT.MS);
				if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
				{
					ERRCHECK(result);
				}

				result = channel.isPlaying(out playing);
				if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
				{
					ERRCHECK(result);
				}

				result = channel.getPaused(out paused);
				if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
				{
					ERRCHECK(result);
				}
			}

			FMODtimerLabel.Text = $"{ms / 1000 / 60}:{ms / 1000 % 60}.{ms / 10 % 100} / {FMODlenms / 1000 / 60}:{FMODlenms / 1000 % 60}.{FMODlenms / 10 % 100}";
			FMODprogressBar.Value = (int)(ms * 1000 / FMODlenms);
			FMODstatusLabel.Text = paused ? "Paused " : playing ? "Playing" : "Stopped";

			if (system != null && channel != null)
			{
				system.update();
			}
		}

		private bool ERRCHECK(FMOD.RESULT result)
		{
			if (result != FMOD.RESULT.OK)
			{
				FMODreset();
				StatusStripUpdate($"FMOD error! {result} - {FMOD.Error.String(result)}");
				return true;
			}
			return false;
		}
		#endregion

		#region GLControl
		private void ChangeGLSize(Size size)
		{
			renderer.UpdateSize(size);
		}

		private void glControl1_Load(object sender, EventArgs e)
		{
			// InitOpenTK();

			renderer = new Renderer(glControl1);
			glControlLoaded = true;
		}

		private void glControl1_Paint(object sender, PaintEventArgs e)
		{
			renderer.Redraw();
		}

		private void tabControl2_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (tabControl2.SelectedIndex == 1 && lastSelectedItem != null)
			{
				dumpTextBox.Text = DumpAsset(lastSelectedItem.Asset);
			}
		}

		private void toolStripMenuItem15_Click(object sender, EventArgs e)
		{
			logger.ShowErrorMessage = toolStripMenuItem15.Checked;
		}

		private void glControl1_MouseWheel(object sender, MouseEventArgs e)
		{
			if (glControl1.Visible)
			{
				renderer.Zoom = renderer.Zoom - (e.Delta * 0.001f);
				glControl1.Invalidate();
			}
		}

		private void glControl1_MouseDown(object sender, MouseEventArgs e)
		{
			mdx = e.X;
			mdy = e.Y;
			if (e.Button == MouseButtons.Left)
			{
				lmdown = true;
			}
			if (e.Button == MouseButtons.Right)
			{
				rmdown = true;
			}
		}

		private void glControl1_MouseMove(object sender, MouseEventArgs e)
		{
			if (lmdown || rmdown)
			{
				float dx = mdx - e.X;
				float dy = mdy - e.Y;
				mdx = e.X;
				mdy = e.Y;

				if (lmdown)
				{
					dx *= 0.01f;
					dy *= 0.01f;

					renderer.Yaw -= dx;
					renderer.Pitch += dy;
				}

				if (rmdown)
				{
					var viewInv = renderer.ViewMatrix;
					var Tinv = viewInv;

					Vector4 e1 = new Vector4(1, 0, 0, 0);
					Vector4 e2 = new Vector4(0, 1, 0, 0);

					e1 = Tinv * e1;
					e2 = Tinv * e2;

					var center = renderer.Center;
					center += e1.Xyz * dx * 0.003f;
					center -= e2.Xyz * dy * 0.003f;

					renderer.Center = center;
				}

				glControl1.Invalidate();
			}
		}

		private void glControl1_MouseUp(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				lmdown = false;
			}
			if (e.Button == MouseButtons.Right)
			{
				rmdown = false;
			}
		}
		#endregion

		// scripts for convenience of analyzing blue archive asset files start here

		// create an index of all assets - it is infeasible (on my pc) to load all the assets,
		// as the memory footprint is way too high, this function will generate an index of all assets
		// from selected .bundle files in csv format for easy lookup etc. 
		private async void generateAssetIndexToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var openFolderDialog = new OpenFolderDialog();
			openFolderDialog.InitialFolder = openDirectoryBackup;
			if (openFolderDialog.ShowDialog(this) == DialogResult.OK)
			{
				var folder = openFolderDialog.Folder;
				await Task.Run(() => UserScripts.ExportAssetIndex(folder, "assetindex.csv"));
			}
		}

		// Animator preview
		// allows previewing animators and animation clips in the viewport
		private void PreviewAnimator(Animator animator)
		{
			// var convert = animationList != null
			//    ? new ModelConverter(m_Animator, Properties.Settings.Default.convertType, animationList.Select(x => (AnimationClip)x.Asset).ToArray())
			//    : new ModelConverter(m_Animator, Properties.Settings.Default.convertType);

			glControl1.Visible = true;
			if (!renderer.SetModel(animator))
			{
				glControl1.Visible = false;
				StatusStripUpdate("Unable to preview this mesh");
			}
			else
			{
				StatusStripUpdate("Using OpenGL Version: " + GL.GetString(StringName.Version) + "\n"
									  + "'Mouse Left'=Rotate | 'Mouse Right'=Move | 'Mouse Wheel'=Zoom \n"
									  + "'Ctrl W'=Wireframe | 'Ctrl S'=Shade | 'Ctrl N'=ReNormal ");
			}
		}
	}
}
