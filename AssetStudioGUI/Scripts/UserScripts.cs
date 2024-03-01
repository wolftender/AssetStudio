using AssetStudio;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.LinkLabel;

namespace AssetStudioGUI.Scripts
{
	internal static class UserScripts
	{
		internal static Action<string> StatusStripUpdate = x => { };

		struct AssetInfo
		{
			public string file;
			public string name;
			public string type;
			public long pathId;
		}

		private static AssetInfo GetAssetNameFromReader(ObjectReader objectReader)
		{
			AssetInfo info = new AssetInfo();

			objectReader.Reset();
			var namedObj = new NamedObject(objectReader);

			switch (objectReader.type)
			{
				case ClassIDType.AnimationClip:
				case ClassIDType.Mesh:
				case ClassIDType.Sprite:
				case ClassIDType.SpriteAtlas:
				case ClassIDType.TextAsset:
				case ClassIDType.Texture2D:
				case ClassIDType.Avatar:
					var name = namedObj.m_Name;
					info.name = name;
					break;
				default:
					info.name = "(no name)";
					break;
			}

			info.pathId = objectReader.m_PathID;
			info.type = objectReader.type.ToString();
			return info;
		}

		public static void ExportAssetIndex(string inputDirectory, string outputFile)
		{
			List<AssetInfo> list = new List<AssetInfo>();
			list.Capacity = 100000;

			StatusStripUpdate($"Exporting asset index to {outputFile}");

			try
			{
				Progress.Reset();
				var files = Directory.GetFiles(inputDirectory, "*.*", SearchOption.AllDirectories);
				for (int i = 0; i < files.Length; i++)
				{
					var file = files[i];
					var filePath = Path.GetDirectoryName(file);

					var reader = new FileReader(file);

					if (reader.FileType == FileType.BundleFile)
					{
						var bundleFile = new BundleFile(reader);
						foreach (var bundleSubfile in bundleFile.fileList)
						{
							var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), bundleSubfile.fileName);
							var subReader = new FileReader(dummyPath, bundleSubfile.stream);
							if (subReader.FileType == FileType.AssetsFile)
							{
								// we dont need the asset manager for anything tbh
								var assetsFile = new SerializedFile(subReader, null);

								foreach (var objectInfo in assetsFile.m_Objects)
								{
									var objectReader = new ObjectReader(assetsFile.reader, assetsFile, objectInfo);

									if (objectReader.type == ClassIDType.AssetBundle)
									{
										var bundle = new AssetBundle(objectReader);
										foreach (var bundleItem in bundle.m_Container)
										{
											AssetInfo assetInfo = new AssetInfo();
											assetInfo.type = objectReader.type.ToString();
											assetInfo.name = bundleItem.Key;
											assetInfo.file = file;
											assetInfo.pathId = objectReader.m_PathID;

											list.Add(assetInfo);
										}
									} 
									else
									{
										var assetInfo = GetAssetNameFromReader(objectReader);
										assetInfo.file = file;

										list.Add(assetInfo);
									}
								}
							}
						}
					}

					reader.Dispose();
					Progress.Report(i + 1, files.Length);
				}

				StatusStripUpdate($"Exporting asset index to {outputFile} is complete!");
			} 
			catch (Exception ex)
			{
				StatusStripUpdate($"Error: {ex.Message}");
			}

			using (StreamWriter outStream = new StreamWriter(outputFile))
			{
				foreach (var objectInfo in list)
				{
					outStream.WriteLine($"{objectInfo.file}; {objectInfo.type}; {objectInfo.name}; {objectInfo.pathId}");
				}
			}
		}
	}
}
