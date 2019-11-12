using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileStorageExportTool
{
    public class ObservableProperty<T> : INotifyPropertyChanged
    {
        private T _value;

        public T Value
        {
            get { return _value; }

            set
            {
                _value = value;
                NotifyPropertyChanged("Value");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        internal void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ObservableProperty()
        {
            _value = default(T);
        }

        public ObservableProperty(T aValue)
        {
            _value = aValue;
        }
    }
}
