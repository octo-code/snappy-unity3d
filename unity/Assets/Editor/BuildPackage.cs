
using System.IO;

using UnityEngine;
using UnityEditor;

public class BuildPackage 
{
	public static string[] m_PackageInputPaths = 
	{
		"Assets/Plugins",
		"Assets/Editor/Snappy"
	};
	public const string m_PackageOutputPath = "../build/snappy.unitypackage";

	[MenuItem("Build/Snappy/Build Package")]
	public static void BuildUnityPackage()
	{
		AssetDatabase.Refresh();

		AssetDatabase.ExportPackage(m_PackageInputPaths, m_PackageOutputPath, ExportPackageOptions.Recurse);
	}
}
