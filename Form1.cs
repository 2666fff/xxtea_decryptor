using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            UpdateOutputPathState();

        }
        
        private void Form1_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            config.AppSettings.Settings["WindowLeft"].Value = Left.ToString();
            config.AppSettings.Settings["WindowTop"].Value = Top.ToString();
            config.AppSettings.Settings["LastSignValue"].Value = textBox_sign.Text;
            config.AppSettings.Settings["LastKEYValue"].Value = textBox_KEY.Text;
            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection(config.AppSettings.SectionInformation.Name);
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
                WriteDetailLog("无法解密，文件长度小于签名--->" + inputFile);
                return false;
            }
            Array.Copy(srcData, tmp, XXTEA_sign.Length);
            for (int i = 0; i < XXTEA_sign.Length; i++)
            {
                if (tmp[i] != XXTEA_sign[i])
                {
                    if (IsOverwriteOriginalMode())
                    {
                        WriteDetailLog("无法解密，原文件未更改--->" + inputFile);
                        return false;
                    }

                    FileHandle.CopyFile(inputFile, outputFile, true); // 强制覆盖
                    WriteDetailLog("无法解密，已复制原始文件--->" + inputFile);
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
                WriteDetailLog("解密失败，检查加密信息--->" + inputFile);
                return false; 
            }
            
            if (data2.Length < 10)
            {
                WriteDetailLog("Decode Failed--->" + inputFile);
            }
            else
            {
                if (mFileHandle.FileWrite(data2, outputFile))
                {
                    WriteDetailLog("解密完成--->" + outputFile);
                    return DeleteInputFileAfterLuacRename(inputFile, outputFile, deleteInputAfterLuacRename);
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
