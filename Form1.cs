using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace XXTEADecrypt
{
    public partial class Form1 : Form
    {
        
        public Form1 mForm1;
        public string  inputPath, outputPath;
        public byte[] XXTEA_sign, XXTEA_KEY;
        public FileHandle mFileHandle ;
        public XXTEAHelp mXXTEAHelp = new XXTEAHelp();
        private string currentLogFilePath = string.Empty;
        private bool logWriteFailed;
        public Form1()
        {
            mForm1 = this;
            mFileHandle = new FileHandle(mForm1, inputPath, outputPath);
            InitializeComponent();
            
            int left = int.Parse(ConfigurationManager.AppSettings["WindowLeft"]?? "0");
            int top = int.Parse(ConfigurationManager.AppSettings["WindowTop"]?? "0");
            this.StartPosition = FormStartPosition.Manual;
            this.Left = left;
            this.Top = top;
            this.Closing += Form1_Closing;
            textBox_sign.Text = ConfigurationManager.AppSettings["LastSignValue"] ?? "";
            textBox_KEY.Text = ConfigurationManager.AppSettings["LastKEYValue"] ?? "";
            bool compressImagesToWebP;
            if (bool.TryParse(ConfigurationManager.AppSettings["CompressImagesToWebP"], out compressImagesToWebP))
            {
                checkBox_compressWebP.Checked = compressImagesToWebP;
            }
            UpdateOutputPathState();

        }
        
        private void Form1_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            config.AppSettings.Settings["WindowLeft"].Value = Left.ToString();
            config.AppSettings.Settings["WindowTop"].Value = Top.ToString();
            config.AppSettings.Settings["LastSignValue"].Value = textBox_sign.Text;
            config.AppSettings.Settings["LastKEYValue"].Value = textBox_KEY.Text;
            EnsureAppSetting(config, "CompressImagesToWebP").Value = checkBox_compressWebP.Checked.ToString();
            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection(config.AppSettings.SectionInformation.Name);
        }

        private KeyValueConfigurationElement EnsureAppSetting(Configuration config, string key)
        {
            KeyValueConfigurationElement setting = config.AppSettings.Settings[key];
            if (setting == null)
            {
                config.AppSettings.Settings.Add(key, string.Empty);
                setting = config.AppSettings.Settings[key];
            }

            return setting;
        }

        private bool IsOverwriteOriginalMode()
        {
            return checkBox_overwriteOriginal.Checked;
        }

        private string GetDefaultOutputPath(string currentInputPath)
        {
            currentInputPath = (currentInputPath ?? string.Empty).Trim().TrimEnd('\\', '/');
            if (currentInputPath.Equals(string.Empty))
            {
                return string.Empty;
            }

            if (FileHandle.FileExists(currentInputPath))
            {
                string inputDirectory = FileHandle.GetDirectoryName(currentInputPath);
                return FileHandle.CombinePath(inputDirectory, "out");
            }

            return FileHandle.CombinePath(currentInputPath, "out");
        }

        private void SyncOutputPathFromInput()
        {
            string currentInputPath = textBox_inputPath.Text.Trim();
            if (currentInputPath.Equals(string.Empty))
            {
                return;
            }

            textBox_outputPath.Text = IsOverwriteOriginalMode() ? currentInputPath : GetDefaultOutputPath(currentInputPath);
        }

        private void UpdateOutputPathState()
        {
            bool overwriteOriginal = IsOverwriteOriginalMode();
            textBox_outputPath.Enabled = !overwriteOriginal;
            button_outputCheck.Enabled = !overwriteOriginal;
            SyncOutputPathFromInput();
        }

        private bool EnsureOutputDirectory()
        {
            if (IsOverwriteOriginalMode())
            {
                return true;
            }

            if (!FileHandle.DirectoryExists(outputPath))
            {
                try
                {
                    FileHandle.CreateDirectory(outputPath);
                    Console.WriteLine("Created directory: " + outputPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("您的输出目录并不是有效路径!");
                    Console.WriteLine("An error occurred while creating directory: " + ex.Message);
                    return false;
                }
            }

            return true;
        }

        private string GetMappedOutputFilePath(string sourceFile, string sourceRoot)
        {
            return IsOverwriteOriginalMode() ? sourceFile : FileHandle.GetOutputPath(sourceFile, sourceRoot, outputPath);
        }

        private bool ShouldRenameLuacToLua(string path)
        {
            return luacToluaCB.Checked &&
                FileHandle.GetExtension(path).Equals(".luac", StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldCompressImagesToWebP()
        {
            return checkBox_compressWebP.Checked;
        }

        private bool DeleteInputFileAfterLuacRename(string inputFile, string outputFile, bool shouldDelete)
        {
            if (!shouldDelete || FileHandle.IsSamePath(inputFile, outputFile))
            {
                return true;
            }

            try
            {
                if (FileHandle.FileExists(inputFile))
                {
                    File.Delete(FileHandle.ToLongPath(inputFile));
                    WriteDetailLog("已删除原 luac 文件--->" + inputFile);
                }

                return true;
            }
            catch (Exception ex)
            {
                WriteDetailLog("删除原 luac 文件失败--->" + inputFile + "，原因：" + ex.Message);
                return false;
            }
        }

        private string GetLogDirectory()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return FileHandle.CombinePath(FileHandle.CombinePath(localAppData, "XXTEADecrypt"), "logs");
        }

        private string GetWebPToolPath()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string outputToolPath = Path.Combine(baseDirectory, "Tools", "convert_smaller_webp.py");
            if (File.Exists(outputToolPath))
            {
                return outputToolPath;
            }

            string sourceToolPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Tools", "convert_smaller_webp.py");
            if (File.Exists(sourceToolPath))
            {
                return sourceToolPath;
            }

            string workingDirectoryToolPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "convert_smaller_webp.py");
            return File.Exists(workingDirectoryToolPath) ? workingDirectoryToolPath : outputToolPath;
        }

        private bool TryCompressImageToWebPIfSmaller(string outputFile)
        {
            if (!ShouldCompressImagesToWebP())
            {
                return true;
            }

            if (string.IsNullOrEmpty(outputFile) || !FileHandle.FileExists(outputFile))
            {
                return true;
            }

            if (!ShouldAttemptWebPCompression(outputFile))
            {
                return true;
            }

            string toolPath = GetWebPToolPath();
            if (!File.Exists(toolPath))
            {
                WriteDetailLog("图片转WebP压缩失败，未找到工具脚本--->" + toolPath);
                return false;
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = "python";
                startInfo.Arguments = QuoteArgument(toolPath) + " " + QuoteArgument(outputFile) + " --quality 85";
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;

                using (Process process = Process.Start(startInfo))
                {
                    string stdout = process.StandardOutput.ReadToEnd().Trim();
                    string stderr = process.StandardError.ReadToEnd().Trim();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        WriteDetailLog("图片转WebP压缩失败--->" + outputFile + "，原因：" + GetProcessMessage(stdout, stderr));
                        return false;
                    }

                    WriteWebPCompressionLog(outputFile, stdout);
                    return true;
                }
            }
            catch (Exception ex)
            {
                WriteDetailLog("图片转WebP压缩异常--->" + outputFile + "，原因：" + ex.Message);
                return false;
            }
        }

        private bool ShouldAttemptWebPCompression(string outputFile)
        {
            byte[] header = ReadFileHeader(outputFile, 16);
            if (header.Length == 0)
            {
                return false;
            }

            if (HasAsciiAt(header, 0, "RIFF") && HasAsciiAt(header, 8, "WEBP"))
            {
                WriteDetailLog("图片已是WebP内容，跳过压缩--->" + outputFile);
                return false;
            }

            return IsUnencryptedImageFile(header);
        }

        private byte[] ReadFileHeader(string path, int length)
        {
            try
            {
                using (FileStream stream = new FileStream(FileHandle.ToLongPath(path), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int count = (int)Math.Min(length, stream.Length);
                    byte[] buffer = new byte[count];
                    int read = stream.Read(buffer, 0, count);
                    if (read == count)
                    {
                        return buffer;
                    }

                    byte[] trimmed = new byte[read];
                    Buffer.BlockCopy(buffer, 0, trimmed, 0, read);
                    return trimmed;
                }
            }
            catch (Exception ex)
            {
                WriteDetailLog("读取图片文件头失败，跳过WebP压缩--->" + path + "，原因：" + ex.Message);
                return new byte[0];
            }
        }

        private string QuoteArgument(string argument)
        {
            if (argument == null)
            {
                return "\"\"";
            }

            return "\"" + argument.Replace("\"", "\\\"") + "\"";
        }

        private string GetProcessMessage(string stdout, string stderr)
        {
            if (!string.IsNullOrEmpty(stderr))
            {
                return stderr;
            }

            return string.IsNullOrEmpty(stdout) ? "未知错误" : stdout;
        }

        private void WriteWebPCompressionLog(string outputFile, string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                return;
            }

            if (output.StartsWith("converted ", StringComparison.OrdinalIgnoreCase))
            {
                WriteDetailLog("图片已转WebP压缩--->" + outputFile + " (" + output + ")");
            }
            else if (output.StartsWith("kept_not_smaller", StringComparison.OrdinalIgnoreCase))
            {
                WriteDetailLog("图片转WebP后不更小，保留原文件--->" + outputFile + " (" + output + ")");
            }
            else if (output.StartsWith("skipped already_webp", StringComparison.OrdinalIgnoreCase))
            {
                WriteDetailLog("图片已是WebP内容，跳过压缩--->" + outputFile);
            }
            else if (output.StartsWith("skipped non_image", StringComparison.OrdinalIgnoreCase))
            {
                WriteDetailLog("非图片文件，跳过WebP压缩--->" + outputFile);
            }
            else if (output.StartsWith("skipped animated", StringComparison.OrdinalIgnoreCase))
            {
                WriteDetailLog("动图文件，跳过WebP压缩--->" + outputFile);
            }
            else
            {
                WriteDetailLog("图片转WebP压缩结果--->" + outputFile + "：" + output);
            }
        }

        private void BeginDetailLog(string operationName)
        {
            logWriteFailed = false;
            currentLogFilePath = string.Empty;

            try
            {
                string logDirectory = GetLogDirectory();
                FileHandle.CreateDirectory(logDirectory);
                string logFileName = "XXTEADecrypt_" + operationName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";
                currentLogFilePath = FileHandle.CombinePath(logDirectory, logFileName);
                string header = "操作：" + operationName + Environment.NewLine +
                    "开始时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                    "输入路径：" + inputPath + Environment.NewLine +
                    "输出路径：" + outputPath + Environment.NewLine +
                    "覆盖原文件：" + (IsOverwriteOriginalMode() ? "是" : "否") + Environment.NewLine +
                    "图片转WebP压缩：" + (ShouldCompressImagesToWebP() ? "是" : "否") + Environment.NewLine +
                    "----------------------------------------" + Environment.NewLine;
                File.WriteAllText(FileHandle.ToLongPath(currentLogFilePath), header, Encoding.UTF8);
                button_openLog.Enabled = true;
            }
            catch (Exception ex)
            {
                button_openLog.Enabled = false;
                richTextBox_log.Text = "日志文件创建失败：" + ex.Message;
            }
        }

        public void WriteDetailLog(string message)
        {
            if (string.IsNullOrEmpty(currentLogFilePath))
            {
                return;
            }

            try
            {
                string line = DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine;
                File.AppendAllText(FileHandle.ToLongPath(currentLogFilePath), line, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                if (!logWriteFailed)
                {
                    logWriteFailed = true;
                    richTextBox_log.Text = richTextBox_log.Text + Environment.NewLine + "日志写入失败：" + ex.Message;
                }
            }
        }

        private void ShowIdleStatus()
        {
            richTextBox_log.Text = "准备就绪。请选择输入路径后开始。";
        }

        private void ShowScanningStatus(string operationName)
        {
            richTextBox_log.Text = operationName + "中..请勿操作... 0%" + Environment.NewLine +
                "正在扫描文件..." + Environment.NewLine +
                "详细日志：" + (string.IsNullOrEmpty(currentLogFilePath) ? "未创建" : currentLogFilePath);
            richTextBox_log.Refresh();
            Application.DoEvents();
        }

        private void ShowProgressStatus(string operationName, int completedCount, int totalCount)
        {
            int percent = totalCount <= 0 ? 0 : (int)Math.Floor(completedCount * 100.0 / totalCount);
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            richTextBox_log.Text = operationName + "中..请勿操作... " + percent + "%" + Environment.NewLine +
                "进度：" + completedCount + "/" + totalCount + Environment.NewLine +
                "详细日志：" + (string.IsNullOrEmpty(currentLogFilePath) ? "未创建" : currentLogFilePath);
            richTextBox_log.Refresh();
            Application.DoEvents();
        }

        private string FormatSpanTime(TimeSpan ts)
        {
            return ts.Hours.ToString() + "小时" + ts.Minutes.ToString() + "分" + ts.Seconds.ToString() + "秒";
        }

        private void ShowFinishedStatus(string operationName, int totalCount, int failedCount, string spanTime)
        {
            string summary = failedCount == 0
                ? "全部完成--->总共" + operationName + "有" + totalCount + "个文件!"
                : "全部完成--->总共" + operationName + "有" + totalCount + "个文件,其中有" + failedCount + "个文件失败或未处理!";

            richTextBox_log.Text = operationName + "完成 100%" + Environment.NewLine +
                summary + Environment.NewLine +
                "耗时：" + spanTime + Environment.NewLine +
                "详细日志：" + (string.IsNullOrEmpty(currentLogFilePath) ? "未创建" : currentLogFilePath);
        }

        private void button_openLog_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentLogFilePath) || !FileHandle.FileExists(currentLogFilePath))
            {
                MessageBox.Show("暂无可打开的日志文件!");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = currentLogFilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开日志失败：" + ex.Message);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            TimeSpan ts1 = new TimeSpan(DateTime.Now.Ticks);

            mFileHandle.fileBox.Clear();
            if (!CheckState()) return;

            Console.WriteLine("输出目录:" + outputPath);

            bool inputIsDirectory = FileHandle.DirectoryExists(inputPath);
            bool inputIsFile = FileHandle.FileExists(inputPath);
            if (!inputIsDirectory && !inputIsFile)
            {
                MessageBox.Show("您的输入路径不是有效的目录或文件!");
                return;
            }

            if (!EnsureOutputDirectory()) return;

            if (inputIsDirectory)
            {
                Console.WriteLine("输入目录:" + inputPath);
                if (!CheckFormat()) return;
                this.Text = "XXTEA解密工具----(解密中...勿操作)";
                BeginDetailLog("解密");
                WriteDetailLog("Start decoding ---> 请勿操作");
                ShowScanningStatus("解密");
                mFileHandle.DirectoryToFile(inputPath);
                WriteDetailLog("Total files found --->" + mFileHandle.fileBox.Count);
            }
            else if (inputIsFile)
            {
                if (!IsOverwriteOriginalMode())
                {
                    mFileHandle.FileToDirctory(inputPath);
                }
                Console.WriteLine("输入路径是文件");
                this.Text = "XXTEA解密工具----(解密中...勿操作)";
                BeginDetailLog("解密");
                WriteDetailLog("Start decoding ---> 请勿操作");
                ShowProgressStatus("解密", 0, 1);
                string inputDirectory = FileHandle.GetDirectoryName(inputPath);
                string singleOutputPath = GetMappedOutputFilePath(inputPath, inputDirectory);
                int failedCount = DecryptFile(inputPath, singleOutputPath) ? 0 : 1;
                ShowProgressStatus("解密", 1, 1);
                TimeSpan singleTs = new TimeSpan(DateTime.Now.Ticks).Subtract(ts1).Duration();
                string singleSpanTime = FormatSpanTime(singleTs);
                WriteDetailLog("--->解密已全部完成共耗时:" + singleSpanTime);
                ShowFinishedStatus("解密", 1, failedCount, singleSpanTime);
                this.Text = "XXTEA解密工具----(解密完成)";
                return;
            }
            //此处开始调用解密函数
            
            int i = 0;
            int completedCount = 0;
            ShowProgressStatus("解密", 0, mFileHandle.fileBox.Count);
            foreach (string mInputPath in mFileHandle.fileBox)
            {
                if (DecryptFile(mInputPath, GetMappedOutputFilePath(mInputPath, inputPath)))
                {
                }
                else
                {
                    i++;
                }
                completedCount++;
                ShowProgressStatus("解密", completedCount, mFileHandle.fileBox.Count);
            }
            if (i == 0)
            {
                WriteDetailLog("全部完成--->总共解密有" + mFileHandle.fileBox.Count + "个文件!");

            }
            else {
                WriteDetailLog("全部完成--->总共解密有" + mFileHandle.fileBox.Count + "个文件,其中有" + i + "个文件没有加密或解密失败!");
            }
            TimeSpan ts2 = new TimeSpan(DateTime.Now.Ticks);
            TimeSpan ts = ts2.Subtract(ts1).Duration(); //时间差的绝对值 
            string spanTime = FormatSpanTime(ts); //以X小时X分X秒的格式现实执行时间

            WriteDetailLog("--->解密已全部完成共耗时:" + spanTime + ",如有任何疑问或建议请联系作者,支持作者请查看\"关于\"");
            ShowFinishedStatus("解密", mFileHandle.fileBox.Count, i, spanTime);
            this.Text = "XXTEA解密工具----(解密完成)";
            

        }

        private void textBox_DeDragDrop(object sender, DragEventArgs e)
        {
            textBox_inputPath.Text = ((System.Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();
        }

        private void textBox_inputPath_TextChanged(object sender, EventArgs e)
        {
            SyncOutputPathFromInput();
        }

        private void textBox_DeDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Link;
            }
        }

        private void textBox_enDragDrop(object sender, DragEventArgs e)
        {
            if (!IsOverwriteOriginalMode())
            {
                textBox_outputPath.Text = ((System.Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();
            }
        }

        private void textBox_enDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string str = ((System.Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();
                if (FileHandle.DirectoryExists(str))
                {
                    e.Effect = DragDropEffects.Link;
                }
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (IsOverwriteOriginalMode())
            {
                return;
            }

            FolderBrowserDialog fbd = new FolderBrowserDialog();
            fbd.ShowDialog();
            if (!fbd.SelectedPath.Equals(""))
            {
                textBox_outputPath.Text = fbd.SelectedPath;
            }

        }

        private void button_inputCheck_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            fbd.ShowDialog();
            if (!fbd.SelectedPath.Equals(""))
            {
                textBox_inputPath.Text = fbd.SelectedPath;
            }
        }

        private void button_encrypt_Click(object sender, EventArgs e)
        {
            TimeSpan ts1 = new TimeSpan(DateTime.Now.Ticks);

            mFileHandle.fileBox.Clear();
            if (!CheckState()) return;

            Console.WriteLine("输出目录:" + outputPath);

            bool inputIsDirectory = FileHandle.DirectoryExists(inputPath);
            bool inputIsFile = FileHandle.FileExists(inputPath);
            if (!inputIsDirectory && !inputIsFile)
            {
                MessageBox.Show("您的输入路径不是有效的目录或文件!");
                return;
            }

            if (!EnsureOutputDirectory()) return;

            if (inputIsDirectory)
            {
                Console.WriteLine("输入目录:" + inputPath);
                if (!CheckFormat()) return;
                this.Text = "XXTEA解密工具----(加密中...勿操作)";
                BeginDetailLog("加密");
                WriteDetailLog("Start encoding ---> 请勿操作");
                ShowScanningStatus("加密");
                mFileHandle.DirectoryToFile(inputPath);
                WriteDetailLog("Total files found --->" + mFileHandle.fileBox.Count);
            }
            else if (inputIsFile)
            {
                if (!IsOverwriteOriginalMode())
                {
                    mFileHandle.FileToDirctory(inputPath);
                }
                Console.WriteLine("输入路径是文件");
                this.Text = "XXTEA解密工具----(加密中...勿操作)";
                BeginDetailLog("加密");
                WriteDetailLog("Start encoding ---> 请勿操作");
                ShowProgressStatus("加密", 0, 1);
                string inputDirectory = FileHandle.GetDirectoryName(inputPath);
                string singleOutputPath = GetMappedOutputFilePath(inputPath, inputDirectory);
                int failedCount = EncryptFile(inputPath, singleOutputPath) ? 0 : 1;
                ShowProgressStatus("加密", 1, 1);
                TimeSpan singleTs = new TimeSpan(DateTime.Now.Ticks).Subtract(ts1).Duration();
                string singleSpanTime = FormatSpanTime(singleTs);
                WriteDetailLog("--->加密已全部完成共耗时" + singleSpanTime);
                ShowFinishedStatus("加密", 1, failedCount, singleSpanTime);
                this.Text = "XXTEA解密工具----(加密完成)";
                return;
            }
            //此处开始调用加密函数
            
            int i = 0;
            int completedCount = 0;
            ShowProgressStatus("加密", 0, mFileHandle.fileBox.Count);
            foreach (string mInputPath in mFileHandle.fileBox)
            {
                string mappedOutputPath = GetMappedOutputFilePath(mInputPath, inputPath);
                Console.WriteLine("-->输出路径:" + mappedOutputPath);
                if (EncryptFile(mInputPath, mappedOutputPath))
                {
                }
                else
                {
                    i++;
                }
                completedCount++;
                ShowProgressStatus("加密", completedCount, mFileHandle.fileBox.Count);
            }
            if (i == 0)
            {
                WriteDetailLog("全部完成--->总共加密有" + mFileHandle.fileBox.Count + "个文件!");
            }
            else {
                WriteDetailLog("全部完成--->总共加密有" + mFileHandle.fileBox.Count + "个文件,其中有" + i + "个文件加密失败!");
            }
            TimeSpan ts2 = new TimeSpan(DateTime.Now.Ticks);
            TimeSpan ts = ts2.Subtract(ts1).Duration(); //时间差的绝对值 
            string spanTime = FormatSpanTime(ts); //以X小时X分X秒的格式现实执行时间
            WriteDetailLog("--->加密已全部完成共耗时" + spanTime + ",如有任何疑问或建议请联系作者,支持作者请查看\"关于\"!");
            ShowFinishedStatus("加密", mFileHandle.fileBox.Count, i, spanTime);
            this.Text = "XXTEA解密工具----(加密完成)";
            
        }

        private void 打开文件ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "lua文件(*.lua)|*.lua|luac文件(*.luac)|*.luac|所有文件(*.*)|*.*";
            ofd.ShowDialog();
            if (!ofd.FileName.Equals(""))
            {
                textBox_inputPath.Text = ofd.FileName;
            }
        }

        private void 退出ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            System.Environment.Exit(0);
        }

        private void menuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {

        }

        private void Form1_Load(object sender, EventArgs e)
        {
            button_openLog.Enabled = false;
            ShowIdleStatus();
        }

        private bool TryMarkUnencryptedRegularFileAsDecrypted(string inputFile, string outputFile, byte[] srcData, bool deleteInputAfterLuacRename)
        {
            string fileType = GetUnencryptedRegularFileType(inputFile, srcData);
            if (string.IsNullOrEmpty(fileType))
            {
                return false;
            }

            try
            {
                FileHandle.CopyFile(inputFile, outputFile, true);
            }
            catch (Exception ex)
            {
                WriteDetailLog("未加密" + fileType + "文件复制失败--->" + outputFile + "，原因：" + ex.Message);
                return false;
            }

            WriteDetailLog("未加密" + fileType + "文件，已按已解密处理--->" + outputFile);
            bool compressed = TryCompressImageToWebPIfSmaller(outputFile);
            bool deletedInput = DeleteInputFileAfterLuacRename(inputFile, outputFile, deleteInputAfterLuacRename);
            return compressed && deletedInput;
        }

        private string GetUnencryptedRegularFileType(string inputFile, byte[] data)
        {
            if (data == null)
            {
                return string.Empty;
            }

            if (IsUnencryptedImageFile(data))
            {
                return "图片";
            }

            if (IsUnencryptedMp3File(inputFile, data))
            {
                return "MP3";
            }

            if (IsUnencryptedMp4File(data))
            {
                return "MP4";
            }

            if (IsUnencryptedWavFile(data))
            {
                return "WAV";
            }

            if (IsLikelyTextFile(inputFile, data))
            {
                return "文本";
            }

            return string.Empty;
        }

        private bool IsUnencryptedImageFile(byte[] data)
        {
            return HasByteSequence(data, 0, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ||
                HasByteSequence(data, 0, new byte[] { 0xFF, 0xD8, 0xFF }) ||
                HasAsciiAt(data, 0, "GIF87a") ||
                HasAsciiAt(data, 0, "GIF89a") ||
                HasAsciiAt(data, 0, "BM") ||
                (HasAsciiAt(data, 0, "RIFF") && HasAsciiAt(data, 8, "WEBP")) ||
                HasByteSequence(data, 0, new byte[] { 0x49, 0x49, 0x2A, 0x00 }) ||
                HasByteSequence(data, 0, new byte[] { 0x4D, 0x4D, 0x00, 0x2A }) ||
                HasByteSequence(data, 0, new byte[] { 0x00, 0x00, 0x01, 0x00 }) ||
                HasAsciiAt(data, 0, "DDS ");
        }

        private bool IsUnencryptedMp3File(string inputFile, byte[] data)
        {
            if (HasAsciiAt(data, 0, "ID3"))
            {
                return true;
            }

            string extension = FileHandle.GetExtension(inputFile);
            return data.Length >= 2 &&
                data[0] == 0xFF &&
                (data[1] & 0xE0) == 0xE0 &&
                extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsUnencryptedMp4File(byte[] data)
        {
            return HasAsciiAt(data, 4, "ftyp");
        }

        private bool IsUnencryptedWavFile(byte[] data)
        {
            return HasAsciiAt(data, 0, "RIFF") && HasAsciiAt(data, 8, "WAVE");
        }

        private bool IsLikelyTextFile(string inputFile, byte[] data)
        {
            if (data.Length == 0)
            {
                return true;
            }

            if (IsLikelyUtf16Text(data))
            {
                return true;
            }

            if (HasZeroByte(data) || HasBinaryControlByte(data))
            {
                return false;
            }

            if (IsStrictUtf8Text(data))
            {
                return true;
            }

            return IsKnownTextExtension(inputFile);
        }

        private bool IsLikelyUtf16Text(byte[] data)
        {
            if (HasByteSequence(data, 0, new byte[] { 0xFF, 0xFE }))
            {
                return IsDecodedText(data, 2, Encoding.Unicode);
            }

            if (HasByteSequence(data, 0, new byte[] { 0xFE, 0xFF }))
            {
                return IsDecodedText(data, 2, Encoding.BigEndianUnicode);
            }

            if (data.Length < 8)
            {
                return false;
            }

            int pairs = data.Length / 2;
            int evenZeroCount = 0;
            int oddZeroCount = 0;
            for (int i = 0; i < pairs * 2; i += 2)
            {
                if (data[i] == 0)
                {
                    evenZeroCount++;
                }

                if (data[i + 1] == 0)
                {
                    oddZeroCount++;
                }
            }

            if (oddZeroCount * 100 >= pairs * 60 && evenZeroCount * 100 <= pairs * 20)
            {
                return IsDecodedText(data, 0, Encoding.Unicode);
            }

            if (evenZeroCount * 100 >= pairs * 60 && oddZeroCount * 100 <= pairs * 20)
            {
                return IsDecodedText(data, 0, Encoding.BigEndianUnicode);
            }

            return false;
        }

        private bool IsStrictUtf8Text(byte[] data)
        {
            try
            {
                UTF8Encoding strictUtf8 = new UTF8Encoding(false, true);
                string text = strictUtf8.GetString(data);
                return HasOnlyTextCharacters(text);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private bool IsDecodedText(byte[] data, int offset, Encoding encoding)
        {
            if (offset >= data.Length)
            {
                return true;
            }

            try
            {
                string text = encoding.GetString(data, offset, data.Length - offset);
                return HasOnlyTextCharacters(text);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private bool HasOnlyTextCharacters(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                if (current == '\uFFFD')
                {
                    return false;
                }

                if (char.IsControl(current) &&
                    current != '\r' &&
                    current != '\n' &&
                    current != '\t' &&
                    current != '\f' &&
                    current != '\b')
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasZeroByte(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasBinaryControlByte(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                byte current = data[i];
                if (current < 32 &&
                    current != 9 &&
                    current != 10 &&
                    current != 12 &&
                    current != 13 &&
                    current != 8)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsKnownTextExtension(string inputFile)
        {
            string extension = FileHandle.GetExtension(inputFile).ToLowerInvariant();
            return extension.Equals(".txt") ||
                extension.Equals(".lua") ||
                extension.Equals(".json") ||
                extension.Equals(".xml") ||
                extension.Equals(".plist") ||
                extension.Equals(".csv") ||
                extension.Equals(".tsv") ||
                extension.Equals(".ini") ||
                extension.Equals(".cfg") ||
                extension.Equals(".conf") ||
                extension.Equals(".properties") ||
                extension.Equals(".js") ||
                extension.Equals(".ts") ||
                extension.Equals(".css") ||
                extension.Equals(".html") ||
                extension.Equals(".htm") ||
                extension.Equals(".md") ||
                extension.Equals(".yml") ||
                extension.Equals(".yaml") ||
                extension.Equals(".glsl") ||
                extension.Equals(".vert") ||
                extension.Equals(".frag") ||
                extension.Equals(".shader") ||
                extension.Equals(".atlas") ||
                extension.Equals(".fnt") ||
                extension.Equals(".tmx") ||
                extension.Equals(".tsx");
        }

        private bool HasAsciiAt(byte[] data, int offset, string value)
        {
            if (data == null || value == null || offset < 0 || data.Length < offset + value.Length)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (data[offset + i] != (byte)value[i])
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasByteSequence(byte[] data, int offset, byte[] value)
        {
            if (data == null || value == null || offset < 0 || data.Length < offset + value.Length)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (data[offset + i] != value[i])
                {
                    return false;
                }
            }

            return true;
        }

        private bool DecryptFile(string inputFile, string outputFile)
        {
            bool deleteInputAfterLuacRename = IsOverwriteOriginalMode() && ShouldRenameLuacToLua(outputFile);
            if (ShouldRenameLuacToLua(outputFile))
            {
                outputFile = FileHandle.ChangeExtension(outputFile, ".lua");
            }
            byte[] srcData = mFileHandle.FileRead(inputFile);  
            byte[] tmp = new byte[XXTEA_sign.Length];
            if (srcData.Length < XXTEA_sign.Length)
            {
                if (TryMarkUnencryptedRegularFileAsDecrypted(inputFile, outputFile, srcData, deleteInputAfterLuacRename))
                {
                    return true;
                }

                WriteDetailLog("无法解密，文件长度小于签名--->" + inputFile);
                return false;
            }
            Array.Copy(srcData, tmp, XXTEA_sign.Length);
            for (int i = 0; i < XXTEA_sign.Length; i++)
            {
                if (tmp[i] != XXTEA_sign[i])
                {
                    if (TryMarkUnencryptedRegularFileAsDecrypted(inputFile, outputFile, srcData, deleteInputAfterLuacRename))
                    {
                        return true;
                    }

                    if (IsOverwriteOriginalMode())
                    {
                        WriteDetailLog("无法解密，原文件未更改--->" + inputFile);
                        return false;
                    }

                    FileHandle.CopyFile(inputFile, outputFile, true); // 强制覆盖
                    WriteDetailLog("无法解密，已复制原始文件--->" + inputFile);
                    TryCompressImageToWebPIfSmaller(outputFile);
                    return false;
                }
            }
            //此处需要去掉文件头的签名值并重新计算数据长度
            uint ret_length;
            int len = srcData.Length - XXTEA_sign.Length;
            byte[] data = new byte[len];
            Buffer.BlockCopy(srcData, XXTEA_sign.Length, data, 0, len);
            byte[] data2 = mXXTEAHelp.xxtea_decrypt(data, (uint)len, XXTEA_KEY, (uint)XXTEA_KEY.Length, out ret_length);
            if (data2 == null)
            {
                if (TryMarkUnencryptedRegularFileAsDecrypted(inputFile, outputFile, srcData, deleteInputAfterLuacRename))
                {
                    return true;
                }

                WriteDetailLog("解密失败，检查加密信息--->" + inputFile);
                return false; 
            }
            
            if (data2.Length < 10)
            {
                if (TryMarkUnencryptedRegularFileAsDecrypted(inputFile, outputFile, srcData, deleteInputAfterLuacRename))
                {
                    return true;
                }

                WriteDetailLog("Decode Failed--->" + inputFile);
            }
            else
            {
                if (mFileHandle.FileWrite(data2, outputFile))
                {
                    WriteDetailLog("解密完成--->" + outputFile);
                    bool compressed = TryCompressImageToWebPIfSmaller(outputFile);
                    bool deletedInput = DeleteInputFileAfterLuacRename(inputFile, outputFile, deleteInputAfterLuacRename);
                    return compressed && deletedInput;
                }
            }
            return false; 
        }

        private void 帮助ToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            Form f = new Form2();
            f.Show();
            
        }

        private void 关于ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form3 f = new Form3();
            f.Show();
        }

        private bool EncryptFile(string inputFile, string outputFile)
        {
            byte[] srcData = mFileHandle.FileRead(inputFile);
            uint ret_length;
            byte[] data = mXXTEAHelp.xxtea_encrypt(srcData, (uint)srcData.Length, XXTEA_KEY, (uint)XXTEA_KEY.Length, out ret_length);
            byte[] data2 = new byte[data.Length + XXTEA_sign.Length];
            Buffer.BlockCopy(XXTEA_sign, 0, data2, 0, XXTEA_sign.Length);
            Buffer.BlockCopy(data, 0, data2, XXTEA_sign.Length, data.Length);
            if (mFileHandle.FileWrite(data2, outputFile))
            {
                WriteDetailLog("加密完成--->" + outputFile);
                return true;
            }
            return false;
        }

        private void richTextBox_log_TextChanged(object sender, EventArgs e)
        {
            richTextBox_log.SelectionStart = richTextBox_log.Text.Length;
            richTextBox_log.ScrollToCaret();
        }

        private void luacToluaCB_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void checkBox_overwriteOriginal_CheckedChanged(object sender, EventArgs e)
        {
            UpdateOutputPathState();
        }

        /// <summary>
        /// 检查输入的内容不为空
        /// </summary>
        /// <returns>检查结果</returns>
        public bool CheckState()
        {
            if (textBox_sign.Text.Equals(""))
            {
                MessageBox.Show("签名值为空,请输入签名值!");
                return false;
            }
            XXTEA_sign = Encoding.ASCII.GetBytes(textBox_sign.Text);
            if (textBox_KEY.Text.Equals(""))
            {
                MessageBox.Show("解密KEY为空,请重新输入!");
                return false;
            }
            XXTEA_KEY = Encoding.ASCII.GetBytes(textBox_KEY.Text);
            if (textBox_inputPath.Text.Equals(""))
            {
                MessageBox.Show("输入路径为空,请重新输入!");
                return false;
            }
            inputPath = textBox_inputPath.Text;
            mFileHandle.inputPath = inputPath;
            if (IsOverwriteOriginalMode())
            {
                outputPath = inputPath;
                textBox_outputPath.Text = inputPath;
            }
            else if (textBox_outputPath.Text.Equals(""))
            {
                MessageBox.Show("输出路径为空,请重新输入!");
                return false;
            }
            else
            {
                outputPath = textBox_outputPath.Text;
            }
            mFileHandle.outputPath = outputPath;         
            return true;
        }
        /// <summary>
        /// 检查选择解密的格式是否正确
        /// </summary>
        /// <returns>返回结果</returns>
        public bool CheckFormat()
        {
            if (!textBox_custom.Text.Equals("") || checkBox_lua.Checked || checkBox_All.Checked || checkBox_zip.Checked || checkBox_png.Checked || checkBox_full.Checked)
            {
                if (checkBox_full.Checked)
                {
                    if (!textBox_custom.Text.Equals("") || checkBox_lua.Checked || checkBox_All.Checked || checkBox_zip.Checked || checkBox_png.Checked)
                    {
                        MessageBox.Show("您选择要解密的文件格式存在重复项,请检查后重新选择!");
                        return false;
                    }
                    else
                    {
                        mFileHandle.strFormat[0] = "*";
                        return true;
                    }
                }
                else if (!textBox_custom.Text.Equals(""))
                {
                    if (textBox_custom.Text.Equals("*.lua") || textBox_custom.Text.Equals("*.luac") || textBox_custom.Text.Equals("*.zip") || textBox_custom.Text.Equals("*.png"))
                    {
                        MessageBox.Show("您输入的自定义格式与勾选项存在重复,请检查后重新输入!");
                        return false;
                    }
                }
                int i = 0;
                if (checkBox_All.Checked)
                {
                    mFileHandle.strFormat[0] = ".*";
                    i++;
                }
                else
                {
                    if (checkBox_lua.Checked)
                    {
                        mFileHandle.strFormat[i] = mFileHandle.WildcardToRegex("*.lua$");
                        i++;
                        mFileHandle.strFormat[i] = mFileHandle.WildcardToRegex("*.luac$");
                        i++;
                    }
                    if (checkBox_zip.Checked)
                    {
                        mFileHandle.strFormat[i] = mFileHandle.WildcardToRegex("*.zip$");
                        i++;
                    }
                    if (checkBox_png.Checked)
                    {
                        mFileHandle.strFormat[i] = mFileHandle.WildcardToRegex("*.png$");
                        i++;
                    }
                    if (!textBox_custom.Text.Equals(""))
                    {
                        mFileHandle.strFormat[i] = mFileHandle.WildcardToRegex("^" + textBox_custom.Text);
                    }
                }
            }
            else
            {
                MessageBox.Show("请选择要解密的文件类型!");
                return false;
            }
            return true;
        }
      
    }
}
