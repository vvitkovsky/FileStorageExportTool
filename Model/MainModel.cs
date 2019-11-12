using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mirasys.Common.Utils;
using Mirasys.Common.Utils.ErrorHandler;
using Mirasys.FileStorage;
using Mirasys.SharedCode.LicenseModel.License;

namespace FileStorageExportTool
{
    internal class MainModel
    {
        private ErrorLogger _logger = Logger.GetLogger(typeof(MainModel));

        public string SelectedPath { get; private set; } = @"C:\temp\";
        public string DestinationPath { get; private set; } = @"C:\temp\";
        public string LicensePath { get; private set; } = string.Empty;
        public bool IsLicenseValid { get; private set; } = false;

        private Task _processTask;
        private CancellationTokenSource _stopTask;

        private object _progressLock = new object();
        private object _indexLock = new object();

        public event EventHandler<GeneralizedEventArgs<string>> ErrorReceived;
        public event EventHandler<GeneralizedEventArgs<double>> ProgressReceived;
        public event EventHandler<GeneralizedEventArgs<bool>> LicenseParsed;
        public event EventHandler ExportCompleteted;

        public MainModel()
        {
            SelectedPath = Properties.Settings.Default.SelectedPath;
            DestinationPath = Properties.Settings.Default.DestinationPath;
            LicensePath = Properties.Settings.Default.LicensePath;
        }

        ~MainModel()
        {
            Properties.Settings.Default.SelectedPath = SelectedPath;
            Properties.Settings.Default.DestinationPath = DestinationPath;
            Properties.Settings.Default.LicensePath = LicensePath;
            Properties.Settings.Default.Save();
        }        

        public void BrowseFolder(BrowseFolderType aBrowseType)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                DialogResult result = dialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    switch (aBrowseType)
                    {
                        case BrowseFolderType.SelectedPath:
                            SelectedPath = dialog.SelectedPath;
                            break;
                        case BrowseFolderType.DestinationPath:
                            DestinationPath = dialog.SelectedPath;
                            break;
                    }
                }
            }
        }

        public void BrowseFile()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = "c:\\";
                dialog.Filter = "lic files (*.lic)|*.lic|All files (*.*)|*.*";
                dialog.RestoreDirectory = true;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    LicensePath = dialog.FileName;
                    ProcessLicenseFile();
                }
            }
        }

        public IEnumerable<Channel> GetChannels(out TimeInterval aTimeInterval)
        {
            aTimeInterval = new TimeInterval();

            MaterialFolderIndex folderIndex = new MaterialFolderIndex();
            Dictionary<ChannelIdentifier, Channel> channels = new Dictionary<ChannelIdentifier, Channel>();
            try
            {
                if (string.IsNullOrEmpty(SelectedPath))
                {
                    return channels.Values;
                }

                if (!Directory.Exists(SelectedPath))
                {
                    SendError("Selected directory is not exists!");
                    return channels.Values;
                }

                string indexFilePath = Path.Combine(SelectedPath, FSConstants.MaterialFolderIndexFile);
                if (!File.Exists(indexFilePath))
                {
                    string path = Path.Combine(SelectedPath, "materials");
                    indexFilePath = Path.Combine(path, FSConstants.MaterialFolderIndexFile);
                    if (File.Exists(indexFilePath))
                    {
                        SelectedPath = path;
                    }
                    else
                    { 
                        SendError("Selected directory doesn't contain index file!");
                        return channels.Values;
                    }
                }

                using (FileStream stream = File.OpenRead(indexFilePath))
                {
                    folderIndex.Deserialize(stream);
                }

                foreach (var index in folderIndex.IndexRecords)
                {
                    if (index.MaterialFileState == FileState.Empty ||
                        index.DataType == DataType.Undefined ||
                        index.DataType == DataType.AlarmLog ||
                        index.ChannelType == ChannelType.Prerecording ||
                        index.ChannelType == ChannelType.Undefined)
                    {
                        continue;
                    }

                    // Update general limits
                    aTimeInterval.UpdateTimeInterval(index.BeginTimestamp, index.EndTimestamp);

                    ChannelIdentifier id = new ChannelIdentifier(index.DataType, index.ChannelId);

                    Channel channel = null;
                    if (!channels.TryGetValue(id, out channel))
                    {
                        channel = new Channel(id);
                        channel.IsEnabled = File.Exists(Path.Combine(SelectedPath, Helpers.GetFileName(index.MaterialFileId)));
                        channels.Add(id, channel);
                    }

                    channel.AddRecord(index);
                }
            }
            catch (Exception ex)
            {
                SendError(ex.Message);
            }
            return channels.Values.OrderBy(x => x.ChannelId.DataType).ThenBy(x => x.ChannelId.Number);
        }

        public bool StartExport(IEnumerable<Channel> aChannels, TimeInterval aTimeInterval)
        {
            if (string.IsNullOrEmpty(SelectedPath))
            {
                SendError("Source path is invalid!");
                return false;
            }

            if (string.IsNullOrEmpty(DestinationPath))
            {
                SendError("Destination path is invalid!");
                return false;
            }

            _stopTask = new CancellationTokenSource();

            _processTask = Task.Run(() =>
            {
                ProcessSelectedChannels(aChannels, aTimeInterval);
            }, 
            _stopTask.Token);

            return true;
        }

        public void StopExport()
        {
            _stopTask?.Cancel();
            
            if (_processTask != null && _processTask.Status == System.Threading.Tasks.TaskStatus.Running)
            {
                _processTask.Wait();
                _processTask.Dispose();
                _processTask = null;
            }

            _stopTask?.Dispose();
            _stopTask = null;
        }

        private string GetChannelName(MaterialFolderIndexRecord aIndexRecord)
        {
            switch (aIndexRecord.DataType)
            {
                case DataType.Audio:
                    return "Audio";
                case DataType.Text:
                    return "Text";
            }
            return "Camera";
        }

        private void ProcessSelectedChannels(IEnumerable<Channel> aChannels, TimeInterval aTimeInterval)
        {
            try
            {
                IEnumerable<Channel> channels = aChannels.Where(x => x.IsChecked);
                int count = channels.Count();
                if (count == 0)
                {
                    return;
                }

                string destination = Path.Combine(DestinationPath, $"Export_{DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff")}");

                if (!Directory.Exists(destination))
                {
                    Directory.CreateDirectory(destination);
                }

                ArchiveInfo archiveInfo = new ArchiveInfo();

                // Set material file id
                int materialFileId = 0;

                // Create new material folder index
                MaterialFolderIndex index = new MaterialFolderIndex() { MaterialFolderId = Guid.NewGuid() };

                double progress = 0;
                double channelProgress = 100.0 / count;

                var parallelOptions = new ParallelOptions() { MaxDegreeOfParallelism = 1 };

                Parallel.ForEach(channels, parallelOptions, (channel) =>
                {
                    if (_stopTask.IsCancellationRequested)
                    {
                        return;
                    }

                    var files = channel.Records.Where(x => !ShouldSkipFile(x, aTimeInterval)).OrderBy(x => x.BeginTimestamp);
                    double fileProgress = channelProgress / files.Count();

                    foreach (var fileIndexRecord in files)
                    {
                        if (_stopTask.IsCancellationRequested)
                        {
                            return;
                        }

                        string sourceFilePath = Path.Combine(SelectedPath, Helpers.GetFileName(fileIndexRecord.MaterialFileId));

                        if (File.Exists(sourceFilePath))
                        {
                            var fileId = Interlocked.Increment(ref materialFileId);

                            // Generate new file name
                            string newFilePath = Path.Combine(destination, Helpers.GetFileName(fileId));

                            _logger.Info($"Source file: {sourceFilePath}, destination file: {newFilePath}");

                            if (File.Exists(newFilePath))
                            {
                                File.SetAttributes(newFilePath, FileAttributes.Normal);
                                File.Delete(newFilePath);
                            }

                            MaterialFolderIndexRecord indexRecord = new MaterialFolderIndexRecord()
                            {
                                ChannelId = fileIndexRecord.ChannelId,
                                DataType = fileIndexRecord.DataType,
                                ChannelType = ChannelType.Material,
                                MaterialFileId = fileId
                            };

                            if (ProcessFile(index.MaterialFolderId, sourceFilePath, indexRecord, aTimeInterval, newFilePath))
                            {
                                lock (_indexLock)
                                {
                                    // Update index
                                    index.IndexRecords.Add(indexRecord);

                                    // Update arhive info
                                    archiveInfo.UpdateArchiveInfo(indexRecord, new AdditionalInfo() { Name = GetChannelName(indexRecord) + " " + indexRecord.ChannelId});

                                    // Flush index time limits
                                    indexRecord.Flush();

                                    // Update file info
                                    archiveInfo.UpdateFileInfo(newFilePath);
                                }
                            }
                        }

                        lock (_progressLock)
                        {
                            progress += fileProgress;
                            SendProgress(progress);
                        }
                    }
                });

                if (index.IndexRecords.Count > 0 && !_stopTask.IsCancellationRequested)
                {
                    SaveMaterialFolderIndex(index, destination);
                    ArchiveInfo.SaveArchiveInfo(archiveInfo, destination);
                }
            }
            catch (Exception ex)
            {
                SendError(ex.Message);
            }
            finally
            {
                SendExportCompleted();
            }
        }

        private bool ShouldSkipFile(MaterialFolderIndexRecord aRecord, TimeInterval aTimeInterval)
        {
            return aRecord.EndTimestamp < aTimeInterval.Begin || aRecord.BeginTimestamp > aTimeInterval.End;
        }

        private bool IsEqual(byte[] aBuffer, int aOffset, byte[] aBufferToCompare)
        {
            for (int pos = 0; pos < aBufferToCompare.Length; ++pos)
            {
                if (aBuffer[aOffset + pos] != aBufferToCompare[pos])
                {
                    return false;
                }
            }
            return true;
        }

        private void CopyBuffer(List<byte> aDestination, byte[] aBuffer, int aStartOffset, int aLength)
        {
            for (int pos = aStartOffset; pos < aLength; ++pos)
            {
                aDestination.Add(aBuffer[pos]);
            }
        }

        private bool ProcessFile(Guid aFolderId, string aSourceFilePath, MaterialFolderIndexRecord aNewRecord, TimeInterval aInterval, string aNewFilePath)
        {
            bool result = false;
            bool waitforIntra = true;
            Guid clientGuid = Guid.NewGuid();
            try
            {
                using (FileStream file = File.OpenRead(aSourceFilePath))
                {                   
                    using (IMaterialFile newFile = MaterialFileFactory.CreateMaterialFile(aFolderId, aNewRecord, aNewFilePath))
                    {
                        int readBytes = 0;
                        int startFrameOffset = 0;
                        bool startFound = false;
                        byte[] buffer = new byte[16 * 1024];

                        DateTime lastFrameTimeStamp = DateTime.MinValue;
                        List<byte> frameBuffer = new List<byte>();                        
                        do
                        {
                            if (_stopTask.IsCancellationRequested)
                            {
                                return false;
                            }

                            startFrameOffset = 0;
                            readBytes = file.Read(buffer, 0, buffer.Length);
                      
                            for (int pos = 0; pos < readBytes - Constants.FrameStart.Length; ++pos)
                            {
                                if (IsEqual(buffer, pos, Constants.FrameStart))
                                {
                                    startFound = true;
                                    startFrameOffset = pos;
                                    frameBuffer.Clear();
                                }

                                if (IsEqual(buffer, pos, Constants.FrameEnd))
                                {
                                    int endFramePos = pos + Constants.FrameEnd.Length;
                                    CopyBuffer(frameBuffer, buffer, startFrameOffset, endFramePos);

                                    startFound = false;
                                    startFrameOffset = 0;

                                    Frame frame = null;
                                    try
                                    {
                                        using (MemoryStream stream = new MemoryStream(frameBuffer.ToArray()))
                                        {
                                            try
                                            {
                                                frame = new Frame();
                                                frame.Deserialize(stream);                                                
                                            }
                                            catch
                                            {
                                                continue;
                                            }

                                            if (frame.Metadata.Timestamp <= lastFrameTimeStamp)
                                            {
                                                // Skip old frames
                                                continue;
                                            }

                                            if (frame.Metadata.ChannelId != aNewRecord.ChannelId || frame.DataType != aNewRecord.DataType)
                                            {
                                                // Skip other data 
                                                continue;
                                            }

                                            if (waitforIntra && !frame.Metadata.IsIntra)
                                            {
                                                // Skip non intra if we wait intra
                                                continue;
                                            }         
                                            
                                            waitforIntra = false;

                                            if (aInterval.Contains(frame.Metadata.Timestamp))
                                            {
                                                MaterialPosition position = null;
                                                WriteStatus writeStatus = newFile.Write(frame, ref position);
                                                if (writeStatus == WriteStatus.OK)
                                                {
                                                    // Update material folder index record
                                                    if (aNewRecord.BeginTimestamp.Ticks == 0)
                                                    {
                                                        aNewRecord.BeginTimestamp = frame.Metadata.Timestamp;
                                                    }
                                                    aNewRecord.EndTimestamp = frame.Metadata.Timestamp;

                                                    // Update result
                                                    result = true;

                                                    // Add to frame times
                                                    lastFrameTimeStamp = frame.Metadata.Timestamp;
                                                }
                                                else
                                                {
                                                    SendError($"Write frame error, status: {writeStatus}, source file {aSourceFilePath}, destination file {aNewFilePath}!");
                                                    waitforIntra = true;
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        SendError(ex.Message);
                                        waitforIntra = true;
                                    }
                                    finally
                                    {
                                        frame?.Release();
                                    }
                                }
                            }

                            if (startFound)
                            {
                                CopyBuffer(frameBuffer, buffer, startFrameOffset, buffer.Length);
                            }

                        } while (readBytes > 0);
                    }
                }
            }
            catch (Exception ex)
            {
                SendError(ex.Message);
            }
            return result;
        }

        private static void SaveMaterialFolderIndex(MaterialFolderIndex aMaterialFolderIndex, string aDestDir)
        {
            using (MemoryManagedStream stream = new MemoryManagedStream(aMaterialFolderIndex.Size, "MaterialFolderIndex buffer", 0))
            {
                aMaterialFolderIndex.Serialize(stream);
                using (FileStream file = File.OpenWrite(Path.Combine(aDestDir, FSConstants.MaterialFolderIndexFile)))
                {
                    ArraySegment<byte> data = stream.Buffer;
                    file.Write(data.Array, data.Offset, aMaterialFolderIndex.Size);
                }
            }
        }

        public void ProcessLicenseFile()
        {
            if (!File.Exists(LicensePath))
            {
                return;
            }

            bool isLicenseValid = false;
            try
            {
                var license = LicenseDisassembler.DisassembleTool(File.ReadAllText(LicensePath));
                if (license != null)
                {
                    isLicenseValid = !license.IsInvalid;
                }
            }
            catch (Exception ex)
            {
                SendError(ex.Message);
            }

            if (!isLicenseValid)
            {
                SendError("License is invalid!");
            }

            SendLicenseParsed(isLicenseValid);
        }

        private void SendLicenseParsed(bool aIsLicenseCorrect)
        {
            LicenseParsed?.Invoke(this, new GeneralizedEventArgs<bool>(aIsLicenseCorrect));
        }

        private void SendError(string aError)
        {
            _logger.Error(aError);
            ErrorReceived?.Invoke(this, new GeneralizedEventArgs<string>(aError));
        }

        private void SendProgress(double aValue)
        {
            ProgressReceived?.Invoke(this, new GeneralizedEventArgs<double>(aValue));
        }

        private void SendExportCompleted()
        {
            _logger.Info("Export complete.");
            ExportCompleteted?.Invoke(this, new EventArgs());
        }
    }
}
