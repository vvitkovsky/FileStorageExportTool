using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;
using Mirasys.FileStorage;

namespace FileStorageExportTool
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private MainModel _model = new MainModel();

        public ObservableCollection<Channel> Channels { get; } = new ObservableCollection<Channel>();
        public ObservableProperty<string> Error { get; } = new ObservableProperty<string>();

        public string SelectedPath => _model.SelectedPath;
        public string DestinationPath => _model.DestinationPath;
        public string LicensePath => _model.LicensePath;

        public ObservableProperty<DateTime> Begin { get; } = new ObservableProperty<DateTime>(DateTime.MinValue);
        public ObservableProperty<DateTime> End { get; } = new ObservableProperty<DateTime>(DateTime.MinValue);

        public ObservableProperty<string> ExportButton { get; } = new ObservableProperty<string>("Export");
        public ObservableProperty<double> Progress { get; } = new ObservableProperty<double>(0.0);

        public ObservableProperty<bool> IsBrowseLicenseEnabled { get; } = new ObservableProperty<bool>(true);
        public ObservableProperty<bool> IsBrowseSourcePathEnabled { get; } = new ObservableProperty<bool>(false);
        public ObservableProperty<bool> IsBrowseDestinationPathEnabled { get; } = new ObservableProperty<bool>(false);
        public ObservableProperty<bool> IsExportButtonEnabled { get; } = new ObservableProperty<bool>(false);
        public ObservableProperty<bool> IsTimePeriodEnabled { get; } = new ObservableProperty<bool>(false);
        public ObservableProperty<bool> IsChannelsListEnabled { get; } = new ObservableProperty<bool>(false);

        private RelayCommand _browseLicenseCommand;
        public RelayCommand BrowseLicenseCommand => _browseLicenseCommand ?? (_browseLicenseCommand = CreateBrowseLicenseCommand());

        private RelayCommand _browseSourceCommand;
        public RelayCommand BrowseSourceCommand => _browseSourceCommand ?? (_browseSourceCommand = CreateBrowseSourceCommand());

        private RelayCommand _browseDestinationCommand;
        public RelayCommand BrowseDestinationCommand => _browseDestinationCommand ?? (_browseDestinationCommand = CreateBrowseDestinationCommand());

        private RelayCommand _exportCommand;
        public RelayCommand ExportCommand => _exportCommand ?? (_exportCommand = CreateExportCommand());

        private RelayCommand<bool?> _selectAllCommand;
        public RelayCommand<bool?> SelectAllCommand => _selectAllCommand ?? (_selectAllCommand = new RelayCommand<bool?>(SelectAll));
        
        public MainViewModel()
        {
            _model.ErrorReceived += OnModel_ErrorReceived;
            _model.ExportCompleteted += OnExportCompleteted;
            _model.ProgressReceived += OnProgressReceived;
            _model.LicenseParsed += OnLicenseParsed;

            _model.ProcessLicenseFile();

            UpdateChannels();
        }

        private void OnLicenseParsed(object sender, GeneralizedEventArgs<bool> e)
        {
            IsBrowseSourcePathEnabled.Value = e.Value;
            IsBrowseDestinationPathEnabled.Value = e.Value;
            IsExportButtonEnabled.Value = e.Value;
            IsTimePeriodEnabled.Value = e.Value;
            IsChannelsListEnabled.Value = e.Value;
        }

        private void OnProgressReceived(object sender, GeneralizedEventArgs<double> e)
        {
            Progress.Value = e.Value;
        }

        private void OnExportCompleteted(object sender, EventArgs e)
        {
            EnableButtons(true);
            ExportButton.Value = "Export";
            Error.Value = "Export complete.";
        }

        private void OnModel_ErrorReceived(object sender, GeneralizedEventArgs<string> e)
        {
            Error.Value = e.Value;
        }

        private void UpdateChannels()
        {
            TimeInterval timeInterval = null;
            Channels.Clear();
            foreach (var channel in _model.GetChannels(out timeInterval))
            {
                channel.CheckedChanged += OnChannelCheckedChanged;
                Channels.Add(channel);
            }

            NotifyPropertyChanged("SelectedPath");
        }

        private IEnumerable<Channel> GetSelectedChannels()
        {
            foreach (var channel in Channels)
            {
                if (channel.IsChecked)
                {
                    yield return channel;
                }
            }
        }

        private void OnChannelCheckedChanged(object sender, EventArgs e)
        {
            DateTime begin = DateTime.MinValue;
            DateTime end = DateTime.MinValue;

            foreach (var channel in GetSelectedChannels())
            {
                if (begin == DateTime.MinValue || begin > channel.Begin)
                {
                    begin = channel.Begin;
                }

                if (end == DateTime.MinValue || end < channel.End)
                {
                    end = channel.End;
                }
            }

            Begin.Value = begin.ToLocalTime();
            End.Value = end.ToLocalTime();
        }

        private RelayCommand CreateBrowseLicenseCommand()
        {
            return new RelayCommand(obj =>
            {
                Error.Value = string.Empty;
                Progress.Value = 0;

                _model.BrowseFile();
                NotifyPropertyChanged("LicensePath");
            });
        }

        private RelayCommand CreateBrowseSourceCommand()
        {
            return new RelayCommand(obj => 
            {
                Error.Value = string.Empty;
                Progress.Value = 0;

                _model.BrowseFolder(BrowseFolderType.SelectedPath);
                NotifyPropertyChanged("SelectedPath");

                UpdateChannels();
            });
        }

        private RelayCommand CreateBrowseDestinationCommand()
        {
            return new RelayCommand(obj =>
            {
                Error.Value = string.Empty;
                Progress.Value = 0;

                _model.BrowseFolder(BrowseFolderType.DestinationPath);
                NotifyPropertyChanged("DestinationPath");                
            });
        }

        private void EnableButtons(bool aEnable)
        {
            IsBrowseLicenseEnabled.Value = aEnable;
            IsBrowseSourcePathEnabled.Value = aEnable;
            IsBrowseDestinationPathEnabled.Value = aEnable;
            IsTimePeriodEnabled.Value = aEnable;
            IsChannelsListEnabled.Value = aEnable;
        }

        private RelayCommand CreateExportCommand()
        {
            return new RelayCommand(obj => 
            {
                Error.Value = string.Empty;
                Progress.Value = 0;

                if (ExportButton.Value == "Export")
                {
                    if (_model.StartExport(GetSelectedChannels(), new TimeInterval(Begin.Value.ToUniversalTime(), End.Value.ToUniversalTime())))
                    {
                        EnableButtons(false);
                        ExportButton.Value = "Stop";
                    }
                }
                else
                {
                    // Enable buttons
                    EnableButtons(true);
                    ExportButton.Value = "Export";
                    _model.StopExport();
                }
            });
        }

        private void SelectAll(bool? aSelect)
        {
            if (aSelect.HasValue)
            {
                foreach (var channel in Channels)
                {
                    channel.IsChecked = aSelect.Value;
                }
            }
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        internal void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion INotifyPropertyChanged
    }
}
