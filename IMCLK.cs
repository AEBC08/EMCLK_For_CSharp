using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace IMCLK
{
    public class Kernrl
    {
        public static string NameToPath(string Name)
        {
            int AtIndex = Name.IndexOf('@');
            string Suffix;
            if (AtIndex != -1)
            {
                Suffix = Name.Substring(AtIndex + 1);
                Name = Name.Substring(0, AtIndex);
            }
            else
            {
                Suffix = "jar";
            }
            string[] parts = Name.Split(':');
            if (parts.Length == 4)
            {
                return $"{parts[0].Replace('.', '/')}/{parts[1]}/{parts[2]}/{parts[1]}-{parts[2]}-{parts[3]}.{Suffix}";
            }
            else if (parts.Length == 3)
            {
                return $"{parts[0].Replace('.', '/')}/{parts[1]}/{parts[2]}/{parts[1]}-{parts[2]}.{Suffix}";
            }
            return "";
        }
        public static string ReplaceFirstN(string Text, string ToReplace, string Replacement, int N)
        {
            int Count = 0;
            int Place = 0;
            while (Count < N)
            {
                Place = Text.IndexOf(ToReplace, Place);
                if (Place == -1)
                    break;
                Text = Text.Substring(0, Place) + Replacement + Text.Substring(Place + ToReplace.Length);
                Place += Replacement.Length;
                Count += 1;
            }
            return Text;
        }
        public static void LaunchMinecraft(string JavaPath, string GamePath, string VersionName, int MaxUseRAM, string PlayerName, string UserType = "Legacy", string AuthUUID = "", string AccessToken = "None", string FirstOptionsLang = "zh_CN", string OptionsLang = "", string LauncherName = "IMCLK", string LauncherVersion = "0.1145", bool OutJvmParams = false)
        {
            string JvmParams = "";
            string Delimiter = ":";  // Class path分隔符
            List<string> NativesList = new List<string> { };
            string SystemType = "";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))  // 判断是否为Windows
            {
                SystemType = "windows";
                Delimiter = ";";
                JvmParams += $"\"{JavaPath}\" -XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump";
                if (Environment.OSVersion.Version.Major == 10)  // 判断Windows版本是否为10(11返回的也是10)
                {
                    JvmParams += " -Dos.name=\"Windows 10\" -Dos.version=10.0";
                }
                if (Environment.Is64BitOperatingSystem)  // 判断是否为64位
                {
                    JvmParams += " -Xms256M";
                }
                else
                {
                    JvmParams += " -Xss256M";
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))  // 判断是否为Linux
            {
                SystemType = "linux";
                JvmParams += $"{JavaPath} -Xms256M";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))  // 判断是否为MacOS(OSX)
            {
                SystemType = "osx";
                JvmParams += $"{JavaPath} -XstartOnFirstThread -Xms256M";
            }
            else
            {
                Console.WriteLine("Unknown's System");
            }
            JvmParams += $" -Xmx{MaxUseRAM}M -XX:+UseG1GC -XX:-UseAdaptiveSizePolicy -XX:-OmitStackTraceInFastThrow -Dfml.ignoreInvalidMinecraftCertificates=True -Dfml.ignorePatchDiscrepancies=True -Dlog4j2.formatMsgNoLookups=true";
            var ReadVersionJson = new StreamReader($"{GamePath}/versions/{VersionName}/{VersionName}.json", new UTF8Encoding(false));  // 读取版本json
            var VersionJson = JsonDocument.Parse(ReadVersionJson.ReadToEnd()).RootElement;
            ReadVersionJson.Close();
            if (VersionJson.TryGetProperty("arguments", out var Arguments))
            {
                if (Arguments.TryGetProperty("jvm", out var ArgumentsJvm))
                {
                    foreach (var Jvm in ArgumentsJvm.EnumerateArray())  // 遍历json中的jvm参数
                    {
                        if (Jvm.ValueKind == JsonValueKind.String)
                        {
                            string JvmArguments = Jvm.GetString()!.Replace(" ", "");
                            if (JvmArguments.Contains("${classpath_separator}"))  // 这个判断针对NeoForged的,为-p参数的依赖两边加双引号
                            {
                                JvmParams += $" \"{JvmArguments}\"";
                            }
                            else
                            {
                                JvmParams += $" {JvmArguments}";
                            }
                        }
                    }
                }
            }
            else if (VersionJson.TryGetProperty("minecraftArguments", out var MinecraftArguments))
            {
                JvmParams += " -Djava.library.path=${natives_directory} -cp ${classpath}";
            }
            string MainClass = "";
            if (JvmParams.Contains("${classpath}"))
            {
                JvmParams += $" {VersionJson.GetProperty("mainClass").GetString()!}";  // 添加游戏主类
            }
            else
            {
                MainClass = VersionJson.GetProperty("mainClass").GetString()!;
            }
            if (VersionJson.TryGetProperty("arguments", out Arguments))
            {
                foreach (var ArgumentsGame in Arguments.GetProperty("game").EnumerateArray())  // 遍历json中的jvm参数
                {
                    if (ArgumentsGame.ValueKind == JsonValueKind.String)
                    {
                        JvmParams += $" {ArgumentsGame.GetString()!.Replace(" ", "")}";
                    }
                }
            }
            else if (VersionJson.TryGetProperty("minecraftArguments", out var MinecraftArguments))
            {
                JvmParams += $" {MinecraftArguments.GetString()!}";
            }
            string ClassPath = "\"";
            List<string> NativesPathCacheList = new List<string> { };
            foreach (var Libraries in VersionJson.GetProperty("libraries").EnumerateArray())  // 遍历依赖
            {
                ClassPath += $"{GamePath}/libraries/{NameToPath(Libraries.GetProperty("name").GetString()!)}{Delimiter}";
                if (Libraries.TryGetProperty("natives", out var NativesInfo) && NativesInfo.TryGetProperty(SystemType, out var CorrectNatives))
                {
                    string NativesPath = Path.GetDirectoryName($"{GamePath}/libraries/{NameToPath(Libraries.GetProperty("name").GetString()!)}")!;
                    if (!NativesPathCacheList.Contains(Path.GetDirectoryName(NativesPath)!))
                    {
                        foreach (var Natives in Directory.GetDirectories(NativesPath))
                        {
                            if (Natives.Contains("natives"))
                            {
                                NativesList.Add($"{NativesPath}/{Natives}");
                            }
                        }
                        NativesPathCacheList.Add(NativesPath);
                    }
                }
            }
            NativesPathCacheList.Clear();
            string VersionJar = "";
            if (File.Exists($"{GamePath}/versions/{VersionName}/{VersionName}.jar"))
            {
                VersionJar = $"{GamePath}/versions/{VersionName}/{VersionName}.jar";
                if (!VersionJson.TryGetProperty("inheritsFrom", out var InHeritsFrom2))
                {
                    ClassPath += VersionJar;
                }
            }
            string AssetIndexId = "";
            if (VersionJson.TryGetProperty("assetIndex", out var AssetIndex) && AssetIndex.TryGetProperty("id", out var IndexId))  // 判断assetIndex id是否存在
            {
                AssetIndexId = IndexId.GetString()!;
            }
            if (VersionJson.TryGetProperty("inheritsFrom", out var InHeritsFrom))  // 判断是否是有Mod加载器的版本
            {
                bool FindVersion = false;
                foreach (var GameVersions in Directory.GetDirectories($"{GamePath}/versions"))
                {
                    var ReadGameJson = new StreamReader($"{GameVersions}/versions/{VersionName}/{VersionName}.json", new UTF8Encoding(false));
                    var GameJson = JsonDocument.Parse(ReadGameJson.ReadToEnd()).RootElement;
                    ReadGameJson.Close();
                    if (GameJson.GetProperty("id").GetString()! == InHeritsFrom.GetString()!)
                    {
                        FindVersion = true;
                        if (GameJson.TryGetProperty("arguments", out Arguments))
                        {
                            foreach (var ArgumentsJvm in Arguments.GetProperty("jvm").EnumerateArray())  // 遍历json中的jvm参数
                            {
                                if (ArgumentsJvm.ValueKind == JsonValueKind.String && !JvmParams.Contains(ArgumentsJvm.GetString()!.Replace(" ", "")))
                                {
                                    string JvmArguments = ArgumentsJvm.GetString()!.Replace(" ", "");
                                    if (JvmArguments.Contains("${classpath_separator}"))  // 这个判断针对NeoForged的,为-p参数的依赖两边加双引号
                                    {
                                        JvmParams += $" \"{JvmArguments}\"";
                                    }
                                    else
                                    {
                                        JvmParams += $" {JvmArguments}";
                                    }
                                }
                            }

                        }
                        else if (GameJson.TryGetProperty("minecraftArguments", out var MinecraftArguments))
                        {
                            JvmParams += " -Djava.library.path=${natives_directory} -cp ${classpath}";
                        }
                        if (MainClass != "")
                        {
                            JvmParams += $" {MainClass}";  // 添加游戏主类
                        }
                        if (GameJson.TryGetProperty("arguments", out Arguments))
                        {
                            foreach (var ArgumentsGame in Arguments.GetProperty("game").EnumerateArray())  // 遍历json中的jvm参数
                            {
                                if (ArgumentsGame.ValueKind == JsonValueKind.String && !JvmParams.Contains(ArgumentsGame.GetString()!.Replace("", "")))
                                {
                                    JvmParams += $" {ArgumentsGame.GetString()!.Replace(" ", "")}";
                                }
                            }
                        }
                        else if (GameJson.TryGetProperty("minecraftArguments", out var MinecraftArguments))
                        {
                            JvmParams += $" {MinecraftArguments.GetString()!}";
                        }
                        foreach (var Libraries in GameJson.GetProperty("libraries").EnumerateArray())  // 遍历依赖
                        {
                            string AClassPath = $"{GamePath}/libraries/{NameToPath(Libraries.GetProperty("name").GetString()!)}{Delimiter}";
                            if (!ClassPath.Contains(AClassPath))
                            {
                                ClassPath += AClassPath;
                            }
                            if (Libraries.TryGetProperty("natives", out var NativesInfo) && NativesInfo.TryGetProperty(SystemType, out var CorrectNatives))
                            {
                                string NativesPath = Path.GetDirectoryName($"{GamePath}/libraries/{NameToPath(Libraries.GetProperty("name").GetString()!)}")!;
                                if (!NativesPathCacheList.Contains(Path.GetDirectoryName(NativesPath)!))
                                {
                                    foreach (var Natives in Directory.GetDirectories(NativesPath))
                                    {
                                        if (Natives.Contains("natives") && !NativesList.Contains(Natives))
                                        {
                                            NativesList.Add($"{NativesPath}/{Natives}");
                                        }
                                    }
                                    NativesPathCacheList.Add(NativesPath);
                                }
                            }
                        }
                        NativesPathCacheList.Clear();
                        if (!File.Exists($"{GamePath}/versions/{VersionName}/{VersionName}.jar") && VersionJar == "")
                        {
                            ClassPath += $"{GamePath}/versions/{InHeritsFrom.GetString()!}/{InHeritsFrom.GetString()!}.jar";
                        }
                        else
                        {
                            ClassPath += VersionJar;
                        }
                        if (AssetIndexId == "")
                        {
                            AssetIndexId = GameJson.GetProperty("assetIndex").GetProperty("id").GetString()!;
                        }
                        break;
                    }
                }
                if (!FindVersion)
                { }
            }
            JvmParams = JvmParams.Replace("${classpath}", ClassPath.Trim(';') + "\"");  // 把-cp参数内容换成拼接好的依赖路径
            JvmParams = ReplaceFirstN(JvmParams, "${library_directory}", $"\"{GamePath}/libraries\"", 1);  // 依赖文件夹路径
            JvmParams = JvmParams.Replace("${assets_root}", $"\"{GamePath}/assets\"");  // 资源文件夹路径
            JvmParams = JvmParams.Replace("${assets_index_name}", AssetIndexId);  // 资源索引ID
            bool FindNativesDir = false;
            foreach (var NativesPath in Directory.GetDirectories($"{GamePath}/versions/{VersionName}"))
            {
                if (NativesPath.Contains("natives"))
                {
                    FindNativesDir = true;
                    JvmParams = JvmParams.Replace("${natives_directory}", $"{GamePath}/versions/{VersionName}/{NativesPath}");  // 依赖库文件夹路径
                    break;
                }
            }
            if (!FindNativesDir)
            {
                Directory.CreateDirectory($"{GamePath}/versions/{VersionName}/natives-{SystemType}");
                foreach (var NativesPath in NativesList)
                {
                    ZipFile.ExtractToDirectory(NativesPath, $"{GamePath}/versions/{VersionName}/natives-{SystemType}");
                }
                foreach (var NotNatives in Directory.GetDirectories($"{GamePath}/versions/{VersionName}"))
                {
                    if (NotNatives.EndsWith(".dll") && File.Exists($"{GamePath}/versions/{VersionName}/natives-{SystemType}/{NotNatives}"))
                    {
                        File.Delete($"{GamePath}/versions/{VersionName}/natives-{SystemType}/{NotNatives}");
                    }
                }
                JvmParams = JvmParams.Replace("${natives_directory}", $"{GamePath}/versions/{VersionName}/natives-{SystemType}");  // 依赖库文件夹路径
            }
            if (!FindNativesDir || OptionsLang != "")
            {
                string OptionsContents = $"lang:{FirstOptionsLang}";
                string Lang = OptionsContents;
                if (OptionsLang != "")
                {
                    Lang = $"lang:{OptionsLang}";
                }
                if (File.Exists($"{GamePath}/versions/{VersionName}/options.txt"))
                {
                    var ReadOptions = new StreamReader($"{GamePath}/versions/{VersionName}/options.txt", new UTF8Encoding(false));
                    OptionsContents = ReadOptions.ReadToEnd();
                    ReadOptions.Close();
                    OptionsContents = Regex.Replace(OptionsContents, @"lang:\S+", Lang);
                }
                var WriteOptions = new StreamWriter($"{GamePath}/versions/{VersionName}/options.txt", false, new UTF8Encoding(false));
                WriteOptions.Write(OptionsContents);
                WriteOptions.Close();
            }
            JvmParams = JvmParams.Replace("${game_directory}", $"\"{GamePath}/versions/{VersionName}\"");  // 游戏文件存储路径
            JvmParams = JvmParams.Replace("${launcher_name}", LauncherName);  // 启动器名字
            JvmParams = JvmParams.Replace("${launcher_version}", LauncherVersion);  // 启动器版本
            JvmParams = JvmParams.Replace("${version_name}", VersionName);  // 版本名字
            JvmParams = JvmParams.Replace("${version_type}", VersionJson.GetProperty("type").GetString()!);  // 版本类型
            JvmParams = JvmParams.Replace("${auth_player_name}", PlayerName);  // 玩家名字
            JvmParams = JvmParams.Replace("${user_type}", UserType);  // 登录方式
            if (UserType == "Legacy")
            {
                if (AuthUUID == "")
                { }
            }
            JvmParams = JvmParams.Replace("${auth_uuid}", "23332333233323332333233323332333");  // 唯一标识符ID
            JvmParams = JvmParams.Replace("${auth_access_token}", AccessToken);  // 正版登录令牌
            JvmParams = JvmParams.Replace("${user_properties}", "{}");  // 老版本的用户配置项
            JvmParams = JvmParams.Replace("${classpath_separator}", Delimiter);  // NeoForged的逆天参数之一,替换为Class path的分隔符就行了
            JvmParams = JvmParams.Replace("${library_directory}", $"{GamePath}/libraries");  // NeoForged的逆天参数之二,获取依赖文件夹路径
            if (VersionJar != "")
            {
                JvmParams = JvmParams.Replace("${primary_jar_name}", Path.GetFileName(VersionJar));  // NeoForged的逆天参数之三,替换为游戏本体JAR文件名就行了
            }
            if (OutJvmParams)
            {
                Console.WriteLine(JvmParams);
            }
        }
    }
}