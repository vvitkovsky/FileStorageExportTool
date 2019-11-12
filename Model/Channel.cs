using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mirasys.FileStorage;

namespace FileStorageExportTool
{
    public class Channel : INotifyPropertyChanged
    {
        private bool _isChecked = false;

        public DateTime Begin { get; private set; } = DateTime.MinValue;
        public DateTime End { get; private set; } = DateTime.MinValue;

        public ChannelIdentifier ChannelId { get; private set; }

        public IList<MaterialFolderIndexRecord> Records { get; } = new List<MaterialFolderIndexRecord>();

        public event EventHandler CheckedChanged;

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        internal void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion INotifyPropertyChanged

        public string Name
        {
            get
            {
                return $"Channel number: {ChannelId.Number}, type: {ChannelId.DataType}, begin: {Utils.DateTimetoString(Begin)}, end: {Utils.DateTimetoString(End)}";
            }
        }

        public bool IsChecked
        {
            get
            {
                return _isChecked;
            }

            set
            {
                _isChecked = value;
                NotifyPropertyChanged("IsChecked");
                CheckedChanged?.Invoke(this, new EventArgs());
            }
        }

        public bool IsEnabled { get; set; }

        public Channel(ChannelIdentifier aChannelId)
        {
            ChannelId = aChannelId;
        }

        public void AddRecord(MaterialFolderIndexRecord aRecord)
        {
            if (aRecord.ChannelId == ChannelId.Number && aRecord.DataType == ChannelId.DataType)
            {
                Records.Add(aRecord);

                if (Begin == DateTime.MinValue || aRecord.BeginTimestamp < Begin)
                {
                    Begin = aRecord.BeginTimestamp;
                }

                if (End == DateTime.MinValue || aRecord.EndTimestamp > End)
                {
                    End = aRecord.EndTimestamp;
                }
            }
        }
    }
}
