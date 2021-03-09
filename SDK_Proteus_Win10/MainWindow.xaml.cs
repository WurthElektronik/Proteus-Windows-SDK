using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.AccessControl;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Foundation.Metadata;
using Windows.Security.Credentials;
using Windows.Storage.Streams;


/// ****************************************************************************************************************
/// Copyright 2020: Würth Elektronik eiSos GmbH & Co. KG
/// 
///  THE SOFTWARE INCLUDING THE SOURCE CODE IS PROVIDED “AS IS”. YOU ACKNOWLEDGE THAT WÜRTH ELEKTRONIK
///  EISOS MAKES NO REPRESENTATIONS AND WARRANTIES OF ANY KIND RELATED TO, BUT NOT LIMITED
///  TO THE NON-INFRINGEMENT OF THIRD PARTIES’ INTELLECTUAL PROPERTY RIGHTS OR THE
///  MERCHANTABILITY OR FITNESS FOR YOUR INTENDED PURPOSE OR USAGE. WÜRTH ELEKTRONIK EISOS DOES NOT
///  WARRANT OR REPRESENT THAT ANY LICENSE, EITHER EXPRESS OR IMPLIED, IS GRANTED UNDER ANY PATENT
///  RIGHT, COPYRIGHT, MASK WORK RIGHT, OR OTHER INTELLECTUAL PROPERTY RIGHT RELATING TO ANY
///  COMBINATION, MACHINE, OR PROCESS IN WHICH THE PRODUCT IS USED. INFORMATION PUBLISHED BY
///  WÜRTH ELEKTRONIK EISOS REGARDING THIRD-PARTY PRODUCTS OR SERVICES DOES NOT CONSTITUTE A LICENSE
///  FROM WÜRTH ELEKTRONIK EISOS TO USE SUCH PRODUCTS OR SERVICES OR A WARRANTY OR ENDORSEMENT
///  THEREOF
///
/// ****************************************************************************************************************
/// 
/// License Terms:
/// See license_terms_SDK_Proteus_Win10.pdf included in this repository.
///
///
/// Sources:
/// https://github.com/orgs/WurthElektronik/
///
/// ****************************************************************************************************************
///
/// Please read the readme.md file included in this project before using.
/// Advanced users required. You must know a basic of Bluetooth Low Energy specific items to understand this App.
///
/// ****************************************************************************************************************
///

namespace WE_eiSos_BluetoothLE
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Watcher and Filter for receving advertise packets from a peripheral device or broadcaster
        /// </summary>
        private BluetoothLEAdvertisementWatcher leWatcher;
        private BluetoothLEAdvertisementFilter leAdvertiseFilter;

        /// <summary>
        /// A collection of Devices/messages found during the scanning procedure with leWatcher.
        /// These deivces may be filtered (when the filter leAdvertiseFilter is active and configured).
        /// They are the datasource & bound to the table in the GUI.
        /// </summary>
        private ObservableCollection<pubBleScanItem> scannedItems;

        /// <summary>
        /// Bluetooth Low Energy - works with Profiles and characteristics. Those are given with unique indicators so called UUIDs
        /// The following 4 are the 128 bit UUIDs used by Proteus modules for generic data transmission.
        /// Proteus may include further standard-UUIDs (e.g. device information service and/or others).
        /// </summary>
        public static readonly Guid PROTEUS_Base_UUID = new Guid("6E400000-C352-11E5-953D-0002A5D5C51B");
        public static readonly Guid PROTEUS_PrimaryService_UUID = new Guid("6E400001-C352-11E5-953D-0002A5D5C51B");
        public static readonly Guid PROTEUS_TX_CHARACTERISTIC_UUID = new Guid("6E400002-C352-11E5-953D-0002A5D5C51B");
        public static readonly Guid PROTEUS_RX_CHARACTERISTIC_UUID = new Guid("6E400003-C352-11E5-953D-0002A5D5C51B");

        /// <summary>
        /// This is the characteristic for sending data to the Proteus radio module
        /// For Multi-Connect applications multiple instances need to be created and maintained
        /// </summary>
        private static GattCharacteristic to_proteus = null;

        /// <summary>
        /// this is the characteristic for receiving data from the Proteus radio module.
        /// To achieve this notifications on this characteristic need to be enabled by the App.
        /// For Multi-Connect applications multiple instances need to be created and maintained
        /// </summary>
        private static GattCharacteristic from_proteus = null;

        /// <summary>
        /// this is an instance of the currently connected Bluetooth Low Energy device.
        /// For Multi-Connect applications multiple instances need to be created and maintained.
        /// </summary>
        private BluetoothLEDevice connectedBluetoothLeDevice = null;
        private GattDeviceServicesResult serviceResult = null;

        /// <summary>
        /// lockbit for received advertise ebents.
        /// As many events may occur during scanning (and even more during active scanning) this prevents hickups during 
        /// the update of the data collection for received advertise and beacon messages.
        /// </summary>
        private readonly object _receivedEventLock = new object();

        private bool isBleConnected = false;
        private bool isBleConnecting = false;
        private bool disconnectRequested = false;
        private bool subscribedForNotifications = false;

        /// <summary>
        /// The Bluetooth LE pin is usually a 6 digit number.
        /// For ease of use the default Proteus pin is hardcoded here.
        /// </summary>
        private readonly string Proteus_default_PIN = "123123";

        private bool isBluetoothLEEneumeratorDeviceAvailable = false;

        /// <summary>
        /// for "auto-linebreak" in the listbox. will be synced to actual width of the listbox.
        /// </summary>
        private int maxStringLength = 100;

        enum RF_SecFlags
        {
            Open,
            JustWorks,
            StaticPasskey,
#if false
#warning "currently these options are not supported by windows RT api for not UWP apps"
            LESC_NumericComparison,
            LESC_Passkey,
#endif
        };

        #region "System Bluetooth Capabilities"
        /// <summary>
        /// Check for some capabilities of the local windows system regarding Bluetooth
        /// </summary>        
        private static bool isSecureConnectionSupported = ApiInformation.IsPropertyPresent("Windows.Devices.Bluetooth.BluetoothLEDevice", "WasSecureConnectionUsedForPairing");
        private static bool isLeSupported = ApiInformation.IsPropertyPresent("Windows.Devices.Bluetooth.BluetoothAdapter", "IsLowEnergySupported");
        private static bool isCentralRoleSupported = ApiInformation.IsPropertyPresent("Windows.Devices.Bluetooth.BluetoothAdapter", "IsCentralRoleSupported");
        private static bool isLeSecureConnectionSupported = ApiInformation.IsPropertyPresent("Windows.Devices.Bluetooth.BluetoothAdapter", "AreLowEnergySecureConnectionsSupported");
        #endregion


        #region "Bluetooth Error Codes Windows RT"
        readonly int E_BLUETOOTH_ATT_WRITE_NOT_PERMITTED = unchecked((int)0x80650003);
        readonly int E_BLUETOOTH_ATT_INVALID_PDU = unchecked((int)0x80650004);
        readonly int E_ACCESSDENIED = unchecked((int)0x80070005);
        readonly int E_DEVICE_NOT_AVAILABLE = unchecked((int)0x800710df); // HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_AVAILABLE)
        #endregion


        /// <summary>
        /// Main Window Function. Called once on App Start.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            this.btn_StartScan.IsEnabled = false;

            scannedItems = new ObservableCollection<pubBleScanItem>();
            scannedItems.OrderBy(x => x.BleMacAddress);
            lstNames.ItemsSource = scannedItems;

            this.tB_PinNumber.MaxLength = 6; /* allow a 6 digit pin */

            if ((isCentralRoleSupported != true) ||
                (isLeSupported != true) ||
                (isLeSecureConnectionSupported != true))
            {
                MessageBox.Show("A must-have feature for this app is not supported by your system (at least one of: central role, BLE). App will be closed.");
                return;
            }


            cB_minSecurityLevel.ItemsSource = Enum.GetValues(typeof(RF_SecFlags));
            cB_minSecurityLevel.SelectedIndex = 0;

            try
            {
                leWatcher = new BluetoothLEAdvertisementWatcher();
                leWatcher.SignalStrengthFilter = new BluetoothSignalStrengthFilter();


#warning "ScanningMode - when beacons or advertise extensions shall be received using BluetoothLEScanningMode.Active is required"
                leWatcher.ScanningMode = BluetoothLEScanningMode.Passive; /* BluetoothLEScanningMode.Passive or BluetoothLEScanningMode.Active */

                leWatcher.SignalStrengthFilter.SamplingInterval = TimeSpan.FromMilliseconds(100);
                leWatcher.SignalStrengthFilter.InRangeThresholdInDBm = -65;
                leWatcher.SignalStrengthFilter.OutOfRangeThresholdInDBm = -80;
                leWatcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromMilliseconds(2000);

                /// register events for BluetoothLEAdvertisementWatcher: received advertisement messages event and BluetoothLEAdvertisementWatcher stopped event.
                leWatcher.Stopped += LeWatcher_OnAdvertisementWatcherStopped;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception :=" + ex.Message);
            }

            Task<bool> checkBtEnabledTask = GetBluetoothIsEnabledAsync();
            checkBtEnabledTask.Wait();
            bool checkResult = checkBtEnabledTask.Result;

            if (checkResult == false)
            {
                /* it seems to be the case that the function always requrns (checkResult == false) when the publised version is run. But that has nothing to say in this case because bluetooth is available. */
                //MessageBox.Show("Caution: GetBluetoothIsEnabledAsync() method did return false. Maybe bluetooth is not active in your device!!", "Warning", MessageBoxButton.OK);
            }


            /* check if the registry contains a name "DriverDesc" with value "Microsoft Bluetooth LE Enumerator" which indicates a bluetooth LE compatible device available on the PC */

            try
            {


                /* DriverDesc.value == "Microsoft Bluetooth LE Enumerator" ??? */
                string keyPath = @"SYSTEM\CurrentControlSet\Control\Class\{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\0003";
                string name = "DriverDesc";
                string value = string.Empty;
                string expectedValue = "Microsoft Bluetooth LE Enumerator";


                using (RegistryKey keys = Registry.LocalMachine.OpenSubKey(keyPath, RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadKey))
                {
                    value = keys.GetValue(name).ToString();
                }


                if (value == expectedValue)
                {
                    isBluetoothLEEneumeratorDeviceAvailable = true;
                }
            }
            catch(Exception registryReadException)
            {
                Console.WriteLine("Exception when accessing registry LE Enumerator Name :=" + registryReadException.Message);
                isBluetoothLEEneumeratorDeviceAvailable = false;
            }


            if (isBluetoothLEEneumeratorDeviceAvailable != true)
            {
                this.addItemToListBox("Warning: registry does not contain the required >Microsoft Bluetooth LE Enumerator< device at the expected position.");
                this.addItemToListBox("Warning: Most likely you Bluetooth does  not support the Low Energy extension.");
                this.addItemToListBox("Warning: Please check the readme.md for Bluetooth depencies.");
            }
            else
            {
                this.addItemToListBox("Info: found >Microsoft Bluetooth LE Enumerator< device.");
            }

            this.btn_StartScan.IsEnabled = true;
        }

        /// <summary>
        /// This task is used to detect if the PC has Bluetooth service enabled.
        /// It does not detect if the Bluetooth device is actually supporting Bluetooth Low Energy (AKA bluetooth 4.2 or newer).
        /// Note: function seems not reliable unless debugging. we always see "returnvalue = false" even if bluetooth is availalbe and enabled.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> GetBluetoothIsEnabledAsync()
        {
            var radios = await Radio.GetRadiosAsync();
            var bluetoothRadio = radios.FirstOrDefault(radio => radio.Kind == RadioKind.Bluetooth);
            var otherRadio = radios.FirstOrDefault(radio => radio.Kind == RadioKind.Other);
            var wifiRadio = radios.FirstOrDefault(radio => radio.Kind == RadioKind.WiFi);

            if (bluetoothRadio == null)
            {
                this.addItemToListBox("Warning: radios.FirstOrDefault found no Bluetooth device.");
            }
            else
            {
                // bluetooth radio found, but disabled.
                if (bluetoothRadio.State != RadioState.On)
                {
                    this.addItemToListBox("Warning: bluetoothRadio seems to be disabled. Enable Bluetooth in windows settings before proceeding.");
                }
            }

            if (otherRadio != null)
            {
                this.addItemToListBox("Info: radios.FirstOrDefault found a Other radio device.");
            }

            if (wifiRadio != null)
            {
                this.addItemToListBox("Info: radios.FirstOrDefault found a WiFi radio device.");
            }

            return (bluetoothRadio != null) && (bluetoothRadio.State == RadioState.On);
        }

        /// <summary>
        /// Add string as new row to ListBox from any thread.
        /// </summary>
        /// <param name="st">content for the new line in the ListBox</param>
        public void addItemToListBox(string st)
        {
            this.Dispatcher.Invoke((Action)(() =>
            {
                if (maxStringLength > st.Length)
                {
                    this.statusBox.Items.Add(st);
                }
                else
                {
                    /* apply "linebreak" to fit text line to listbox width */

            string sub = st.Substring(0, maxStringLength);
                    this.statusBox.Items.Add(sub);
                    string reststring = st.Substring(maxStringLength, st.Length - maxStringLength);
                    addItemToListBox(reststring);


                }

                /* scroll to last entry in statusBox */
                if (this.statusBox.Items.Count > 1)
                {
                    this.statusBox.SelectedIndex = (this.statusBox.Items.Count - 1);
                    this.statusBox.ScrollIntoView(this.statusBox.SelectedItem);
                }
            }
            ));
        }


        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        /// <summary>
        /// Calculate the size of a string inside the statusBox.
        /// is used for calculatin the limit of chars per string that fits into one line.
        /// </summary>
        /// <param name="candidate"></param>
        /// <returns></returns>
        private Size MeasureString(string candidate)
        {
            var formattedText = new FormattedText(
                candidate,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(this.statusBox.FontFamily, this.statusBox.FontStyle, this.statusBox.FontWeight, this.statusBox.FontStretch),
                this.statusBox.FontSize,
                Brushes.Black,
                new NumberSubstitution(),
                1);

            return new Size(formattedText.Width, formattedText.Height);
        }

        #region "Events: Direct user Action in GUI"

        /// <summary>
        /// Called when GUI button Start Scan is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btn_StartScan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if ((leWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Created) ||
                    (leWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Stopped))
                {
                    this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_ApplyUUIDFilter.IsEnabled = false));
                    this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_ApplyRSSIFilter.IsEnabled = false));

                    this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_StartScan.IsEnabled = false));
                    this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_StopScan.IsEnabled = true));


                    leAdvertiseFilter = new BluetoothLEAdvertisementFilter();
                    if (cb_ApplyUUIDFilter.IsChecked == true)
                    {
                        /* filter for PROTEUS module advertisements, only */
                        leAdvertiseFilter.Advertisement.ServiceUuids.Add(PROTEUS_PrimaryService_UUID); /* Proteus primary service */
                    }
                    else
                    {
                        /* nothing to do */
                    }

                    leWatcher.SignalStrengthFilter = new BluetoothSignalStrengthFilter();
                    leWatcher.SignalStrengthFilter.SamplingInterval = TimeSpan.FromMilliseconds(100);
                    leWatcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromMilliseconds(2000);
                    if (cb_ApplyRSSIFilter.IsChecked == true)
                    {
                        leWatcher.SignalStrengthFilter.InRangeThresholdInDBm = -70;
                        leWatcher.SignalStrengthFilter.OutOfRangeThresholdInDBm = -83;
                    }
                    else
                    {
                        /* nothing to do */
                    }

                    leWatcher.AdvertisementFilter = leAdvertiseFilter;
                    leWatcher.Received += LeWatcher_OnAdvertisementReceived;
                    leWatcher.Start();

                    while (leWatcher.Status != BluetoothLEAdvertisementWatcherStatus.Started)
                    {
                        /// wait till watcher was started.
                    }

                    Task.Delay(100); /* 100 millisecond delay just to be safe */
                    
                    if (leWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                    {
                        addItemToListBox("Advertise watcher started.");
                    }
                }
            }
            catch (Exception ex3)
            {
                addItemToListBox("BLE Scan could not be used.\r\n" + "ERROR:  " + ex3.Message.ToString());

                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_ApplyUUIDFilter.IsEnabled = true));
                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_ApplyRSSIFilter.IsEnabled = true));

                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_StartScan.IsEnabled = true));
                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_StopScan.IsEnabled = false));

                addItemToListBox("\r\n" + "Enable Bluetooth function in devicemanager and check that a Bluetooth LE compatible device is correctly installed in your PC.");
            }
        }

        /// <summary>
        /// Called when GUI button Stop Scan is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btn_StopScan_Click(object sender, RoutedEventArgs e)
        {
            if (leWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
            {
                leWatcher.Stop();
                leWatcher.Received -= LeWatcher_OnAdvertisementReceived;
                if ((this.scannedItems != null) && (this.scannedItems.Count > 0))
                {
                    this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Connect.IsEnabled = true));
                    this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_PairingEnabled.IsEnabled = true));

                    if (this.scannedItems.Count == 1)
                    {
                        this.lstNames.SelectedIndex = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Called when GUI button Connect is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btn_Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (leWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                {
                    /// a connect is currently not allowed as scanning is running.
                    /// unless you want multi connect this check can be active.
                }
                else
                {
                    /* no scan running, connection process may start */

                    this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Connect.IsEnabled = false));
                    this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_PairingEnabled.IsEnabled = false));

                    if ((lstNames.Items.Count == 0) ||
                        (this.lstNames.SelectedIndex < 0))
                    {
                        return;
                    }

                    if ((isBleConnecting == true) ||
                        (isBleConnected == true))
                    {
                        return;
                    }

                    /* detect selected device in scan list */

                    ulong selected_remote_mac = ulong.Parse(scannedItems[this.lstNames.SelectedIndex].BleMacAddress, System.Globalization.NumberStyles.HexNumber);
                    //ulong selected_remote_mac = 0x0018da000002;

                    disconnectRequested = false;
                    ConnectProteusDevice(selected_remote_mac);
                }
            }
            catch (Exception ex4)
            {
                addItemToListBox("BLE Connect could not be used.\r\n" + "ERROR:  " + ex4.Message.ToString());

                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_ApplyUUIDFilter.IsEnabled = true));
                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_ApplyRSSIFilter.IsEnabled = true));

                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_StartScan.IsEnabled = true));
                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_StopScan.IsEnabled = false));

                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Connect.IsEnabled = false));
                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Disconnect.IsEnabled = false));

                addItemToListBox("\r\n" + "Enable Bluetooth function in devicemanager and check that a Bluetooth LE compatible device is correctly installed in your PC.");
            }
        }

        /// <summary>
        /// Called when GUI button Disconnect is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btn_Disconnect_Click(object sender, RoutedEventArgs e)
        {
            /// just set the flag - the event will be called when disconnected
            this.disconnectRequested = true;
        }

        /// <summary>
        /// Called when GUI button Send data is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btn_Send_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (isBleConnected != true)
                {
                    return;
                }

                /// payload is taken from textbox (i.e. hexadecimal string)
                byte[] payload = Converters.StringToByteArray(this.tb_TxPayload.Text);

                ///StringToByteArray() will return null when: sting has not even number of characters or string is longer than max MTU for Proteus.
                if (payload != null)
                {
                    /// Note: in Bluetooth Low Energy (4.0) compatibility mode: payload size is limited to 19 bytes!
                    /// Bluetooth 4.2 in your PC is required for larger packets.
                    SendData(payload);
                }
                else
                {
                    addItemToListBox("TX Proteus: illegal payload size. Payload must be even and smaller than 243 byte.");
                }
            }
            catch (Exception ex5)
            {
                addItemToListBox("BLE SendTo could not be used.\r\n" + "ERROR:  " + ex5.Message.ToString());
                addItemToListBox("\r\n" + "Enable Bluetooth function in devicemanager and check that a Bluetooth LE compatible device is correctly installed in your PC.");
            }
        }

        /// <summary>
        /// Check user input in the payload textbox if entered characters are compatible to hex character set. They must be 0..9, a-z or A-Z.
        /// </summary>
        /// <param name="sender">this.tb_TxPayload.Text</param>
        /// <param name="e">TextCompositionEventArgs for textbox</param>
        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            int hexNumber;
            e.Handled = !int.TryParse(e.Text, NumberStyles.HexNumber, CultureInfo.CurrentCulture, out hexNumber); /* e.handled = false in case invalid character is entered. allowed: a-f, A-f, 0-9 */
        }

        #endregion "Direct user Action in GUI"

        #region "Events: Bluetooth LE Advertisment related"

        /// <summary>
        /// Is triggered when a advertise watcher task is stopped.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void LeWatcher_OnAdvertisementWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            if (args.Error == BluetoothError.Success)
            {
                addItemToListBox("Advertise watcher stopped.");
            }
            else
            {
                addItemToListBox("Error: " + args.Error.ToString());
            }

            ///GIU modifications due to stopped LE advertise scanner
            this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_ApplyUUIDFilter.IsEnabled = true));
            this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_ApplyRSSIFilter.IsEnabled = true));

            this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_StopScan.IsEnabled = false));
            this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_StartScan.IsEnabled = true));
        }

        /// <summary>
        /// Is triggered when a advertise packet is received.
        /// This event may be filtered by selected filters (e.g. UUID, RSSI, MAC address, Devicename, ....) and not show any advertising BLE device.
        /// </summary>
        /// <param name="sender">le advertise watcher the event was triggered by</param>
        /// <param name="args">arguments of the event</param>
        private void LeWatcher_OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            try
            {
                /// create temporary paramters for scanned item class
                short rssi = args.RawSignalStrengthInDBm;
                DateTimeOffset timestamp = args.Timestamp;
                ulong btAddress = args.BluetoothAddress;
                string localName = args.Advertisement.LocalName;

                IList<Guid> uuidList = args.Advertisement.ServiceUuids;

                /// create temporary ble scan result item
                pubBleScanItem temp = new pubBleScanItem(btAddress.ToString("X12"), localName, rssi, timestamp.ToLocalTime().ToString("HH:mm:ss.fff")) { };

                lock (_receivedEventLock)
                {
                    this.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                    {
                        /// an item in the collection is uniquely identifyable only using the Bluetooth MAC address.
                        pubBleScanItem item = scannedItems.FirstOrDefault(x => (x.BleMacAddress == temp.BleMacAddress));
                        if (item != null)
                        {
                            /* item is already in the collection -> change it! */

                            if ((args.AdvertisementType == BluetoothLEAdvertisementType.ConnectableDirected) || (args.AdvertisementType == BluetoothLEAdvertisementType.ConnectableUndirected))
                            {
                                /// is the received signal strenth valid ?
                                /// discard scan results with invalid rssi.
                                if (rssi > -127)
                                {
                                    item.Update(temp.Timestamp, temp.Name, temp.Rssi);
                                }
                            }
                        }
                        else
                        {
                            /* item is not yet in the collection -> add it! */

                            if (rssi > -127)
                            {
                                scannedItems.Add(temp);
                            }
                        }
                    }
                    ));
                }
            }
            catch (NullReferenceException ex)
            {
                ex.ToString();
            }
        }

        #endregion "Bluetooth LE Advertisment related Events"

        #region "Connect to Proteus BLE device"

        /// <summary>
        /// Connect to Proteus BLE device
        /// </summary>
        /// <param name="mac">6 byte MAC BLE adresse</param>
        private async void ConnectProteusDevice(ulong mac)
        {
            isBleConnected = false;
            isBleConnecting = true;

            try
            {
                /// Note: BluetoothLEDevice.FromIdAsync must be called from a UI thread because it may prompt for consent.
                connectedBluetoothLeDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(mac, BluetoothAddressType.Public);
            }
            catch (Exception ex) when (ex.HResult == E_DEVICE_NOT_AVAILABLE)
            {
                addItemToListBox("Info: Bluetooth seems to be disabled / off.");

                isBleConnected = false;
                isBleConnecting = false;
                connectedBluetoothLeDevice?.Dispose();
                connectedBluetoothLeDevice = null;

                await Task.Delay(1000);
                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Disconnect.IsEnabled = false));
                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Connect.IsEnabled = true));
                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.tb_TxPayload.Text = string.Empty));
                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_PairingEnabled.IsEnabled = true));
                return; /* do not continue with connect */

            }

            connectedBluetoothLeDevice.ConnectionStatusChanged += this.OnConnectionStatusChanged;
            connectedBluetoothLeDevice.GattServicesChanged += this.OnGattServicesChanged;

            #region "Connect with Pairing + Security"
            if (isSecureConnectionSupported)
            {
                /* this device is able to securely pair to another device */

                if (connectedBluetoothLeDevice.DeviceInformation.Pairing.IsPaired == true)
                {
                    /* is already paired */
                    switch (connectedBluetoothLeDevice.DeviceInformation.Pairing.ProtectionLevel)
                    {
                        case DevicePairingProtectionLevel.EncryptionAndAuthentication:
                            {
                                addItemToListBox("Info: Paired, EncryptionAndAuthentication was established.");
                            }
                            break;
                        case DevicePairingProtectionLevel.Encryption:
                            {
                                addItemToListBox("Info: Paired, Encryption was established.");
                            }
                            break;
                        case DevicePairingProtectionLevel.None:
                            {
                                addItemToListBox("Info: Paired, but No Encryption or Authentication was established.");
                            }
                            break;
                        default:
                            {
                                addItemToListBox("Info: Paired, level: " + connectedBluetoothLeDevice.DeviceInformation.Pairing.ProtectionLevel.ToString());
                            }
                            break;
                    }
                }
                else if ((connectedBluetoothLeDevice.DeviceInformation.Pairing.CanPair == true) &&
                        (connectedBluetoothLeDevice.DeviceInformation.Pairing.IsPaired == false) &&
                        (this.cb_PairingEnabled.IsChecked == true))
                {
                    /* is not paired so far, but pairing is possible and requested */

                    connectedBluetoothLeDevice.DeviceInformation.Pairing.Custom.PairingRequested += CustomOnPairingRequested;
                    addItemToListBox("Info: Pairing to device now.");
                    addItemToListBox("Info: Duration of this pairing process is depending on the connection interval of the communication partners. Waiting ...");

                    ///change required security method (e.g. static passkey, just works, bonding, pairing), to match Proteus in peripheral only mode defaults (bonding + static passkey) :
                    /// (DevicePairingKinds.ProvidePin, DevicePairingProtectionLevel.EncryptionAndAuthentication)

                    DevicePairingKinds pairingKinds = DevicePairingKinds.ProvidePin;
                    DevicePairingProtectionLevel minpairinglevel = DevicePairingProtectionLevel.EncryptionAndAuthentication;

#warning "RF_SecFlags.StaticPasskey requires pairing+bonding to be active in the counterpart (RF_SecFlags= 0x0B in case of Proteus) or you will get a <handler not registered error>."

                    RF_SecFlags userSelected_RF_SecFlags = (RF_SecFlags)Enum.Parse(typeof(RF_SecFlags), this.cB_minSecurityLevel.SelectedValue.ToString());


                    switch (userSelected_RF_SecFlags)
                    {
                        case RF_SecFlags.Open:
                            {
                                pairingKinds = DevicePairingKinds.None;
                                minpairinglevel = DevicePairingProtectionLevel.None;
                            }
                            break;
                        case RF_SecFlags.JustWorks:
                            {
                                pairingKinds = DevicePairingKinds.ConfirmOnly;
                                minpairinglevel = DevicePairingProtectionLevel.Encryption;
                            }
                            break;
                        case RF_SecFlags.StaticPasskey:
                            {
                                pairingKinds = DevicePairingKinds.ProvidePin;
                                minpairinglevel = DevicePairingProtectionLevel.EncryptionAndAuthentication;
                            }
                            break;
#if false
#warning "so far not clear whether Win10 supports LE secure connection pairing"
                        case RF_SecFlags.LESC_NumericComparison:
                            {
                                pairingKinds = DevicePairingKinds.ConfirmPinMatch;
                                minpairinglevel = DevicePairingProtectionLevel.EncryptionAndAuthentication;
                            }
                            break;
#warning "so far not clear whether Win10 supports LE secure connection pairing"
                        case RF_SecFlags.LESC_Passkey:
                            {
                                pairingKinds = DevicePairingKinds.ProvidePin;
                                minpairinglevel = DevicePairingProtectionLevel.EncryptionAndAuthentication;
                            }
                            break;
#endif
                        default:
                            {
                                /* not supported */
                                isBleConnected = false;
                                isBleConnecting = false;
                                ///This should not happen.
                                addItemToListBox("Error: Security mode " + userSelected_RF_SecFlags.ToString() + " not supported.");

                                connectedBluetoothLeDevice.ConnectionStatusChanged -= this.OnConnectionStatusChanged;
                                connectedBluetoothLeDevice.GattServicesChanged -= this.OnGattServicesChanged;

                                await Task.Delay(1000);
                                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Disconnect.IsEnabled = false));
                                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Connect.IsEnabled = true));
                                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.tb_TxPayload.Text = string.Empty));
                                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_PairingEnabled.IsEnabled = true));
                                return; /* do not continue with connect */
                            }
                            //break;
                    }


                    var result = await connectedBluetoothLeDevice.DeviceInformation.Pairing.Custom.PairAsync(pairingKinds, minpairinglevel);
                    connectedBluetoothLeDevice.DeviceInformation.Pairing.Custom.PairingRequested -= CustomOnPairingRequested;

                    switch (result.Status)
                    {
                        case DevicePairingResultStatus.Paired:
                        ///intentionally no "break;" here!
                        case DevicePairingResultStatus.AlreadyPaired:
                            {
                                // The pairing takes some time to complete. If you don't wait you may have issues.

                                //System.Threading.Thread.Sleep(1500); //1.5 second delay. this is ok for default settings of Proteus. slower connection intervals require more waiting here (try 5 sec.)
                                connectedBluetoothLeDevice.Dispose();

                                //Reload device so that the GATT services are there. This is why we wait.
                                connectedBluetoothLeDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(mac, BluetoothAddressType.Public);
                                addItemToListBox("Info: device is already paired.");
                            }
                            break;

                        case DevicePairingResultStatus.RequiredHandlerNotRegistered:
                            {
                                isBleConnected = false;
                                isBleConnecting = false;
                                ///This should not happen.
                                addItemToListBox("Error: Disconnected during pairing with device. Status: " + result.Status.ToString());
                                addItemToListBox("Info: Please check if bonding in the Proteus is enabled: RF_SecFlags.SECFLAGS_BONDING_ENABLE = '1' before retry connecting");

                                connectedBluetoothLeDevice.ConnectionStatusChanged -= this.OnConnectionStatusChanged;
                                connectedBluetoothLeDevice.GattServicesChanged -= this.OnGattServicesChanged;

                                await Task.Delay(1000);
                                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Disconnect.IsEnabled = false));
                                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Connect.IsEnabled = true));
                                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.tb_TxPayload.Text = string.Empty));
                                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_PairingEnabled.IsEnabled = true));
                                return; /* do not continue with connect */
                            }
                        default:
                            {
                                isBleConnected = false;
                                isBleConnecting = false;
                                ///This should not happen.
                                addItemToListBox("Error: Disconnected during pairing with device. Status: " + result.Status.ToString());
                                addItemToListBox("Info: Please retry connect with pairing.");

                                connectedBluetoothLeDevice.ConnectionStatusChanged -= this.OnConnectionStatusChanged;
                                connectedBluetoothLeDevice.GattServicesChanged -= this.OnGattServicesChanged;

                                await Task.Delay(1000);
                                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Disconnect.IsEnabled = false));
                                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Connect.IsEnabled = true));
                                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.tb_TxPayload.Text = string.Empty));
                                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_PairingEnabled.IsEnabled = true));
                                return; /* do not continue with connect */
                            }
                    }

                    switch (connectedBluetoothLeDevice.DeviceInformation.Pairing.ProtectionLevel)
                    {
                        case DevicePairingProtectionLevel.EncryptionAndAuthentication:
                            {
                                addItemToListBox("Info: Paired, EncryptionAndAuthentication was established.");
                            }
                            break;
                        case DevicePairingProtectionLevel.Encryption:
                            {
                                addItemToListBox("Info: Paired, Encryption was established.");
                            }
                            break;
                        case DevicePairingProtectionLevel.None:
                            {
                                addItemToListBox("Info: Paired, but No Encryption or Authentication was established.");
                            }
                            break;
                        default:
                            {
                                addItemToListBox("Info: Paired, level: " + connectedBluetoothLeDevice.DeviceInformation.Pairing.ProtectionLevel.ToString());
                            }
                            break;
                    }
                }
                else
                {
                    /* pairing not requested -> nothing to do */
                }
            }
            else
            {
                /* secure pairing not supported by central (this device) -> this may lead to problems, in case the Proteus module only allows secure connections */
                addItemToListBox("Secure pairing not supported by this device");
            }
            #endregion

            await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_UnPairingEnabled.IsEnabled = true));

            #region "Get Characteristics and enable Notification"
            serviceResult = await connectedBluetoothLeDevice.GetGattServicesForUuidAsync(PROTEUS_PrimaryService_UUID, BluetoothCacheMode.Uncached);

            if (serviceResult.Status == GattCommunicationStatus.Success)
            {
                foreach (var s in serviceResult.Services)
                {
                    addItemToListBox("Found service UUID: " + s.Uuid);

                    if (s.Uuid == PROTEUS_PrimaryService_UUID)
                    {
                        GattCharacteristicsResult characteristicResult = await s.GetCharacteristicsAsync();
                        if (characteristicResult.Status == GattCommunicationStatus.Success)
                        {
                            foreach (GattCharacteristic c in characteristicResult.Characteristics)
                            {
                                switch (c.Uuid)
                                {
                                    case var r when (r == PROTEUS_TX_CHARACTERISTIC_UUID):
                                        {
                                            /// for data sending to_proteus
                                            addItemToListBox("Found Proteus TX Characteristic: " + c.Uuid);
                                            GattCharacteristicProperties properties = c.CharacteristicProperties;

                                            if (properties.HasFlag(GattCharacteristicProperties.Write))
                                            {
                                                /// This characteristic supports writing to it.
                                                to_proteus = c;

                                                /// sending to Proteus is possible now!
                                                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Send.IsEnabled = true));
                                            }
                                            else
                                            {
                                                /// error
                                                to_proteus = null;
                                            }
                                        }
                                        break;
                                    case var r when (r == PROTEUS_RX_CHARACTERISTIC_UUID):
                                        {
                                            /// for data receiving from_proteus
                                            addItemToListBox("Found Proteus RX Characteristic: " + c.Uuid);
                                            GattCharacteristicProperties properties = c.CharacteristicProperties;

                                            if (true == properties.HasFlag(GattCharacteristicProperties.Notify))
                                            {
                                                try
                                                {
                                                    /// This characteristic supports subscribing to notifications.
                                                    from_proteus = c;

                                                    /// Proteus requires Notifications enabled!
                                                    GattCommunicationStatus status = await from_proteus.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                                                    if (status == GattCommunicationStatus.Success)
                                                    {
                                                        /// notifications were enabled, register value changed event for received data from Proteus device
                                                        subscribedForNotifications = true;
                                                        from_proteus.ValueChanged += Characteristic_ValueChanged;
                                                        addItemToListBox("Enabled notifications for Proteus RX Characteristic.");
                                                    }
                                                    else
                                                    {
                                                        /// error: cannot enable notifications
                                                        from_proteus = null;
                                                    }
                                                }
                                                catch (Exception ex001)
                                                {
                                                    if (this.cb_PairingEnabled.IsChecked == false)
                                                    {
                                                        addItemToListBox("Connected device requires security, but security + pairing was not selected in the App.");
                                                        MessageBox.Show(ex001.Message);
                                                        from_proteus = null;
                                                    }
                                                    else
                                                    {
                                                        addItemToListBox("Connected device requires security, but security connection failed (wrong pin?).");
                                                        MessageBox.Show(ex001.Message);
                                                        from_proteus = null;
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                /// errror: property notification flag missing in property
                                                from_proteus = null;
                                            }
                                        }
                                        break;
                                    default:
                                        {
                                            addItemToListBox("Found unknown characteristic: " + c.Uuid);
                                        }
                                        break;
                                }
                            }
                        }
                        else
                        {
                            addItemToListBox("Error reading charateristics of service UUID: " + s.Uuid);
                        }
                    }
                }
                #endregion

                #region "Channel Open - comminucation with Proteus"
                if ((from_proteus == null) || (to_proteus == null))
                {
                    disconnectRequested = true;
                    isBleConnecting = false;
                    isBleConnected = false;
                }
                else
                {
                    /* connection success */

                    isBleConnecting = false;
                    isBleConnected = true;

                    /* detect negotiated PDU and calculate max supported user payload */
                    uint maxPduSize_ul = from_proteus.Service.Session.MaxPduSize;
                    uint maxPduSize_dl = to_proteus.Service.Session.MaxPduSize;
                    uint maxPduSize = (maxPduSize_ul >= maxPduSize_dl) ? maxPduSize_dl : maxPduSize_ul; /* select smaller of both - though they should be equal */
                    addItemToListBox("Info: Negotiated  PDU = " + maxPduSize.ToString() + "bytes");
                    addItemToListBox("Info: Max user payload = (PDU - 4) = " + (maxPduSize - 4).ToString() + "bytes");

                    addItemToListBox("Indication: Channel open");
                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Disconnect.IsEnabled = true));

                    while (disconnectRequested == false)
                    {
                        /// wait till disconnect event comes from user action in the GUI in intervals of 10 ms
                        await Task.Delay(10);
                    }
                }
                #endregion

                #region "Disconnect"
                if (disconnectRequested == true)
                {
                    connectedBluetoothLeDevice.ConnectionStatusChanged -= this.OnConnectionStatusChanged;
                    connectedBluetoothLeDevice.GattServicesChanged -= this.OnGattServicesChanged;


                    if (this.cb_UnPairingEnabled.IsChecked == true)
                    {
                        addItemToListBox("Indication: Unpairing device. please wait...");
                        await connectedBluetoothLeDevice.DeviceInformation.Pairing.UnpairAsync();
                        await Task.Delay(1000);
                    }

                    ///
                    /// perform gentle disconnect:
                    ///1st check if notifications are still on, if so - disable notifications
                    ///
                    if (subscribedForNotifications == true)
                    {
                        try
                        {

                            /// unregister value changed event
                            from_proteus.ValueChanged -= Characteristic_ValueChanged;

                            /// disable notifications
                            GattCommunicationStatus status = await from_proteus.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None); /* Proteus requires Notifications enabled!" */
                            if (status == GattCommunicationStatus.Success)
                            {
                                addItemToListBox("Notifications for received data from Proteus disabled.");
                                subscribedForNotifications = false;
                            }
                            else
                            {
                                Console.WriteLine("Error while disabling notifications");
                            }
                        }
                        catch (Exception userAborted)
                        {
                            ///this exception will be hit almost any time when "disconnect".
                            //addItemToListBox("aborted by user - notification disabling incomplete.");
                            Console.WriteLine(userAborted.Message);
                        }
                    }


                    /// sending is not possible due to disconnect.
                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Send.IsEnabled = false));

                    isBleConnected = false;

                    /// Microsoft statement: For Bluetooth LE devices our APIs do not provide direct control over the connection to the device.
                    /// Instead our stack will disconnect the device after a one second timeout if there are no outstanding references to it.
                    /// ref: https://social.msdn.microsoft.com/Forums/sqlserver/en-US/9eae39ff-f6ca-4aa9-adaf-97450f2b4a6c/disconnect-bluetooth-low-energy?forum=wdk

                    ///
                    /// dispose all services that are still open
                    ///
                    foreach (var ser in serviceResult.Services)
                    {
                        ser?.Dispose();
                    }

                    ///
                    /// disconnect device and mark memory for GC
                    ///
                    connectedBluetoothLeDevice?.Dispose();

                    serviceResult = null;
                    from_proteus = null;
                    to_proteus = null;
                    connectedBluetoothLeDevice = null;

                    addItemToListBox("Disconnected from Proteus.");

                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    await Task.Delay(1000); /* according to microsoft: wait 1000ms for disconnect ble device happens */

                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Disconnect.IsEnabled = false));
                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Connect.IsEnabled = true));
                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.tb_TxPayload.Text = string.Empty));
                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_PairingEnabled.IsEnabled = true));
                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.cb_UnPairingEnabled.IsEnabled = false));
                }
                else
                {
                    /// should not land here...
                    throw new NotImplementedException();
                }
                #endregion
            }
            else
            {
                /// should not land here...
                throw new NotImplementedException();
            }
        }

        #endregion

        #region "Events: while connected"

        private void OnGattServicesChanged(BluetoothLEDevice sender, object args)
        {
            /// currently unused
        }

        private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                addItemToListBox("Event: Connected to device.");
            }
            else if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                addItemToListBox("Event: Disconnected from device.");
            }
        }

        #endregion

        #region "Event: Pairing/Bonding"

        /// <summary>
        /// Event called during the Pairing process.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void CustomOnPairingRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
        {
            // Console.WriteLine("CustomOnPairingRequested event hit.");
            /// complete example, see: https://github.com/microsoft/Windows-universal-samples/blob/b1cb20f191d3fd99ce89df50c5b7d1a6e2382c01/Samples/DeviceEnumerationAndPairing/cs/Scenario9_CustomPairDevice.xaml.cs#L316

            switch (args.PairingKind)
            {
                case DevicePairingKinds.ProvidePin:
                    {
                        /// the pin used or shown by the device must be used here.
                        /// For Proteus we auto-reply with the default pin to have a fast timing.
#warning "Using the Static Passkey Pin here, can be hardcoded if needed."

                        string text = string.Empty;
                        System.Windows.Application.Current.Dispatcher.Invoke(
                            DispatcherPriority.Normal,
                            (ThreadStart)delegate { text = this.tB_PinNumber.Text; });

                        string staticPasskey = Proteus_default_PIN;
                        uint temp = 0;
                        if (uint.TryParse(text,out temp)) /* must be a 6 digit - numeric only pin. 6 digit long is not checked so far! */
                        {
                            args.Accept(text);
                        }
                        else
                        {
                            args.Accept(Proteus_default_PIN); /* enter Proteus default pin = 123123 */
                        }                        
                    }
                    break;

                case DevicePairingKinds.None:
                    {
                        args.Accept();
                    }
                    break;
                case DevicePairingKinds.ConfirmOnly:
                    {
                        args.Accept();
                    }
                    break;
                case DevicePairingKinds.ConfirmPinMatch:
                    {
                        addItemToListBox("Pairing request. Automatically confirmed pin match: " + args.Pin.ToString());
                        args.Accept();
                    }
                    break;
                case DevicePairingKinds.DisplayPin:
                    {
                        addItemToListBox("Pairing request. Enter pin on other device: " + args.Pin.ToString());
                        args.Accept();
                    }
                    break;
                case DevicePairingKinds.ProvidePasswordCredential:
                    {
                        addItemToListBox("Pairing request ProvidePasswordCredential not implemented");

                        PasswordCredential passwordCredential = new PasswordCredential();
                        passwordCredential.UserName = "testuser";
                        passwordCredential.Password = "testpassword";
                        args.AcceptWithPasswordCredential(passwordCredential);
                    }
                    break;
                default:
                    {
                        addItemToListBox("Pairing request " + args.PairingKind.ToString() + " not implemented");
                    }
                    break;
            }
        }

        #endregion "Pairing/Bonding"

        #region "Send data to Proteus BLE device"

        /// <summary>
        /// Send payload to Proteus device.
        /// Using the Proteus Header
        /// </summary>
        /// <param name="payload">user payload byte array</param>
        private async void SendData(byte[] payload)
        {
            /// check if characteristic is avialble and ble device is connected
            if ((isBleConnected != true) || (to_proteus == null))
            {
                return;
            }

            await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.tb_TxPayload.Text = string.Empty));

            var writer = new DataWriter();
            byte[] toSend = new byte[payload.Length + 1];

            toSend[0] = 0x01; /* add header "Proteus payload data" */
            Array.ConstrainedCopy(payload, 0, toSend, 1, payload.Length);
            writer.WriteBytes(toSend);

            try
            {
                GattCommunicationStatus result = await to_proteus.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithResponse); /* WriteWithResponse will result true if ack was received or perform retrys internally. */
                if (result == GattCommunicationStatus.Success)
                {
                    // Successfully wrote to device
                    addItemToListBox("TX Proteus payload data: 0x" + Converters.ByteArrayToString(toSend.Skip(1).ToArray())); /* first byte of payload is representing command byte header of the protocol used in proteus. - here: 0x01 for "data" */
                }
                else
                {
                    addItemToListBox("TX Proteus payload data failed.");
                }
            }
            catch (Exception ex) when (ex.HResult == E_BLUETOOTH_ATT_INVALID_PDU)
            {
                addItemToListBox("TX Proteus payload data failed. Invalid PDU, too big.");
            }

        }

        #endregion "Send data to Proteus BLE device"

        #region "Event: Received data from Proteus BLE device"

        /// <summary>
        /// ValueChanged event handler for connected BLE device. aka: "receive data" in Bluetooth LE terminology.
        /// Note: Notifications must be enabled during connect!
        /// </summary>
        /// <param name="sender">ble device notifying the changed value</param>
        /// <param name="args">event arguments </param>
        public void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] output = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(output);
            var timestamp = args.Timestamp;

            byte cmd = output[0];
            switch (cmd)
            {
                case 0x01: /* Proteus protocol header = "data" */
                    addItemToListBox("RX Proteus payload data: 0x" + Converters.ByteArrayToString(output.Skip(1).ToArray()));
                    break;

                case 0x04: /* Proteus protocol header = "data + high throughput mode" */
                    addItemToListBox("RX Proteus payload data (high throughput mode) - time: " + timestamp.Second.ToString() + ":" + timestamp.Millisecond.ToString() + " - 0x" + Converters.ByteArrayToString(output.Skip(1).ToArray()));
                    break;

                default: /* Proteus protocol header = "other" */
                    addItemToListBox("RX Proteus cmd(0x" + cmd.ToString("X2") + ") - data: 0x" + Converters.ByteArrayToString(output.Skip(1).ToArray()));
                    break;
            }
        }

        #endregion "Received data from Proteus BLE device"

    }
}