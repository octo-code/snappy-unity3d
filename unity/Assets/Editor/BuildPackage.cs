
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
		string lIOSPluginsPath = "Assets/Editor/Snappy/iOS/src";

		Directory.CreateDirectory(lIOSPluginsPath);

		File.Copy("../snappy/snappy.cc",                Path.Combine(lIOSPluginsPath, "snappy.cc"), true);
		File.Copy("../snappy/snappy.h",                 Path.Combine(lIOSPluginsPath, "snappy.h"), true);
		File.Copy("../snappy/snappy-c.cc",              Path.Combine(lIOSPluginsPath, "snappy-c.cc"), true);
		File.Copy("../snappy/snappy-c.h",               Path.Combine(lIOSPluginsPath, "snappy-c.h"), true);
		File.Copy("../snappy/snappy-internal.h",        Path.Combine(lIOSPluginsPath, "snappy-internal.h"), true);
		File.Copy("../snappy/snappy-sinksource.cc",     Path.Combine(lIOSPluginsPath, "snappy-sinksource.cc"), true);
		File.Copy("../snappy/snappy-sinksource.h",      Path.Combine(lIOSPluginsPath, "snappy-sinksource.h"), true);
		File.Copy("../snappy/snappy-stubs-internal.cc", Path.Combine(lIOSPluginsPath, "snappy-stubs-internal.cc"), true);
		File.Copy("../snappy/snappy-stubs-internal.h",  Path.Combine(lIOSPluginsPath, "snappy-stubs-internal.h"), true);
		File.Copy("../snappy/snappy-stubs-public.h",    Path.Combine(lIOSPluginsPath, "snappy-stubs-public.h"), true);

		AssetDatabase.Refresh();

		AssetDatabase.ExportPackage(m_PackageInputPaths, m_PackageOutputPath, ExportPackageOptions.Recurse);

		Directory.Delete(lIOSPluginsPath, true);

		AssetDatabase.Refresh();
	}
}
