using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WE_eiSos_BluetoothLE
{

    public class pubBleScanItem : IEquatable<pubBleScanItem>, INotifyPropertyChanged
    {
        private string name;
        private short rssi;
        private string bleMacAddress;
        private string timestamp;

        public pubBleScanItem (string BleMacAddress, string Name, short Rssi, string Timestamp)
        {
            timestamp = Timestamp;
            rssi = Rssi;
            name = Name;
            bleMacAddress = BleMacAddress;            
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Name
        {
            get { return name; }

        }
        public short Rssi
        {
            get { return rssi; }

        }
        public string BleMacAddress
        {
            get { return bleMacAddress; }

        }

        public string Timestamp
        {
            get { return timestamp; }

        }

        public void Update(string Timestamp, string Name, short Rssi)
        {

            if (timestamp != Timestamp)
            {
                timestamp = Timestamp;
                NotifyPropertyChanged("Timestamp");
            }

            if (rssi != Rssi)
            {
                rssi = Rssi;
                NotifyPropertyChanged("Rssi");
            }

            if (name != Name)
            {
                name = Name;
                NotifyPropertyChanged("Name");
            }
        }

        public bool Equals(pubBleScanItem other)
        {
            if (other == null)
            {
                return false;
            }

            return (BleMacAddress == other.BleMacAddress);
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

    }
}
