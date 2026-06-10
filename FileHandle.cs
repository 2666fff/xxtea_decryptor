using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

public class FileHandle{
    public List<string> fileBox = new List<string>();
    public string[] strFormat = new string[5];
    public XXTEADecrypt.Form1 mForm1;
    public string outputPath;
    public string inputPath;

    public FileHandle(XXTEADecrypt.Form1 mform,string inputPath, string outputPath)
    {
        this.mForm1 = mform;
        this.outputPath = outputPath;
        this.inputPath = inputPath;
    }

    public static string ToLongPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        path = path.Replace('/', '\\');

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + path.Substring(2);
        }

        if (path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
        {
            return @"\\?\" + path;
        }

        return @"\\?\" + Path.GetFullPath(path);
    }

    public static string GetDirectoryName(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        path = ToNormalPath(path).Replace('/', '\\').TrimEnd('\\');
        int index = path.LastIndexOf('\\');
        if (index == 2 && path.Length >= 2 && path[1] == ':')
        {
            return path.Substring(0, 3);
        }

        return index > 0 ? path.Substring(0, index) : string.Empty;
    }

    public static string GetFileName(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        path = ToNormalPath(path).Replace('/', '\\').TrimEnd('\\');
        int index = path.LastIndexOf('\\');
        return index >= 0 ? path.Substring(index + 1) : path;
    }

    public static string GetExtension(string path)
    {
        string fileName = GetFileName(path);
        int dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 ? fileName.Substring(dotIndex) : string.Empty;
    }

    public static string ChangeExtension(string path, string extension)
    {
        string directory = GetDirectoryName(path);
        string fileName = GetFileName(path);
        int dotIndex = fileName.LastIndexOf('.');
        if (dotIndex >= 0)
        {
            fileName = fileName.Substring(0, dotIndex);
        }

        return string.IsNullOrEmpty(directory) ? fileName + extension : directory + "\\" + fileName + extension;
    }

    public static string CombinePath(string left, string right)
    {
        if (string.IsNullOrEmpty(left))
        {
            return right;
        }

        if (string.IsNullOrEmpty(right))
        {
            return left;
        }

        return left.TrimEnd('\\', '/') + "\\" + right.TrimStart('\\', '/');
    }

    private static string NormalizeForCompare(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string normalized = ToNormalPath(path).Replace('/', '\\').TrimEnd('\\');
        return normalized;
    }

    public static bool IsSamePath(string path1, string path2)
    {
        string normalizedPath1 = NormalizeForCompare(path1);
        string normalizedPath2 = NormalizeForCompare(path2);
        return !string.IsNullOrEmpty(normalizedPath1) &&
            normalizedPath1.Equals(normalizedPath2, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSameOrSubPath(string path, string root)
    {
        string normalizedPath = NormalizeForCompare(path);
        string normalizedRoot = NormalizeForCompare(root);
        if (string.IsNullOrEmpty(normalizedPath) || string.IsNullOrEmpty(normalizedRoot))
        {
            return false;
        }

        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(normalizedRoot + "\\", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetOutputPath(string sourcePath, string sourceRoot, string targetRoot)
    {
        string normalizedSource = ToNormalPath(sourcePath).Replace('/', '\\');
        string normalizedSourceRoot = ToNormalPath(sourceRoot).Replace('/', '\\').TrimEnd('\\');
        string normalizedTargetRoot = ToNormalPath(targetRoot).Replace('/', '\\').TrimEnd('\\');

        if (normalizedSource.Equals(normalizedSourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedTargetRoot;
        }

        string rootPrefix = normalizedSourceRoot + "\\";
        if (normalizedSource.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return CombinePath(normalizedTargetRoot, normalizedSource.Substring(rootPrefix.Length));
        }

        string sourceDirectory = GetDirectoryName(normalizedSource);
        string fileName = GetFileName(normalizedSource);
        if (!string.IsNullOrEmpty(sourceDirectory) &&
            sourceDirectory.Equals(normalizedSourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            return CombinePath(normalizedTargetRoot, fileName);
        }

        return normalizedSource.Replace(sourceRoot, targetRoot);
    }

    public static string ToNormalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
        {
            return @"\\" + path.Substring(8);
        }

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path.Substring(4);
        }

        return path;
    }

    public static bool DirectoryExists(string path)
    {
        return Directory.Exists(ToLongPath(path));
    }

    public static bool FileExists(string path)
    {
        return File.Exists(ToLongPath(path));
    }

    public static void CreateDirectory(string path)
    {
        Directory.CreateDirectory(ToLongPath(path));
    }

    public static void CopyFile(string sourceFileName, string destFileName, bool overwrite)
    {
        if (IsSamePath(sourceFileName, destFileName))
        {
            return;
        }

        string destDirectory = GetDirectoryName(destFileName);
        if (!string.IsNullOrEmpty(destDirectory) && !DirectoryExists(destDirectory))
        {
            CreateDirectory(destDirectory);
        }

        File.Copy(ToLongPath(sourceFileName), ToLongPath(destFileName), overwrite);
    }

	public byte[] FileRead(string path)
	{
        try
        {
            string longPath = ToLongPath(path);
            FileStream fs = new FileStream(longPath, FileMode.Open);
            long len = fs.Length;
            byte[] buffer = new byte[len];
            fs.Read(buffer, 0, (int)len);
            fs.Flush();
            fs.Close();
            return buffer;
        }
        catch (Exception)
        {
            mForm1.WriteDetailLog("读文件错误--->" + path);
            byte[] a = new byte[3];
            return a;
        }

        
	}
    public bool FileWrite(byte[] data, string pathAndName)
    {
        try
        {
            string directory = GetDirectoryName(pathAndName);
            if (!string.IsNullOrEmpty(directory) && !DirectoryExists(directory))
            {
                CreateDirectory(directory);
            }

            FileStream fs = new FileStream(ToLongPath(pathAndName), FileMode.Create);
            fs.Write(data, 0, data.Length);
            fs.Flush();
            fs.Close();
        }
        catch (Exception)
        {
            mForm1.WriteDetailLog("写入失败--->" + pathAndName);
            return false;
        }

        
        return true;
    }
    public void FileToDirctory(string filePath)
    {
        Console.WriteLine(GetDirectoryName(filePath));
        if (!DirectoryExists(outputPath))
        {
            CreateDirectory(outputPath);
        }
    }
	/// <summary>
    /// 获取指定目录下的所有符合条件的文件的绝对路径
    /// </summary>
    /// <param name="inputfolder">传入的目录</param>
    /// <param name="suffix">过滤条件</param>
    /// <returns>符合条件的文件的绝对路径字符串数组</returns>
    public void DirectoryToFile(string inputFolder)
	{
		string absoultPath;
        //获取当前目录的文件
        if (strFormat[0].Equals(".*"))
        {
            foreach (string nextFile in Directory.GetFiles(ToLongPath(inputFolder)))
            {
                absoultPath = ToNormalPath(nextFile);
                fileBox.Add(absoultPath);
            }
        }
        else
        {
            foreach (string nextFile in Directory.GetFiles(ToLongPath(inputFolder)))
            {
                string tmp = GetFileName(nextFile);
                for (int i = 0; strFormat[i] != null; i++)
                {
                    if (StrMatching(tmp, strFormat[i]))
                    {
                        absoultPath = ToNormalPath(nextFile);
                        fileBox.Add(absoultPath);
                        break;
                    }  
                }        
            }
        }
        string tmp2;
		foreach(string nextFolder in Directory.GetDirectories(ToLongPath(inputFolder)))
		{
            string normalNextFolder = ToNormalPath(nextFolder);
            if (!IsSamePath(inputPath, outputPath) && IsSameOrSubPath(normalNextFolder, outputPath))
            {
                Console.WriteLine("跳过输出目录:" + normalNextFolder);
                continue;
            }

            tmp2 = GetOutputPath(normalNextFolder, inputPath, outputPath);
			Console.WriteLine("获取到子目录:" + normalNextFolder + ",待写入目录:" + tmp2);
            if (!DirectoryExists(tmp2))
            {
                CreateDirectory(tmp2);
            }
            this.DirectoryToFile(normalNextFolder);
		}
	}
    
    /// <summary>
    /// 将通配符中的*和?转换为正则表达式
    /// </summary>
    /// <param name="strWildcard">待转换字符串</param>
    /// <returns>转换的结果</returns>
    public string WildcardToRegex(string strWildcard)
    {
        strWildcard = strWildcard.Replace(".", @"\.");
        strWildcard = strWildcard.Replace("?", @".[1]");
        strWildcard = strWildcard.Replace("*", @".*");
       // strWildcard = "^" + strWildcard;
        return strWildcard;
    }

    public bool StrMatching(string src, string strRule)
    {
        return Regex.IsMatch(src, strRule);
    }
    
}
