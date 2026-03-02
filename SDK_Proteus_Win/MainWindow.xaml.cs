using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.AccessControl;
using System.Text.RegularExpressions;
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
using Windows.Storage.Streams;

/// ****************************************************************************************************************
/// Copyright 2026: Würth Elektronik eiSos GmbH & Co. KG
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
/// See license_terms_SDK_Proteus_Win.pdf included in this repository.
///
///
/// Sources:
/// https://github.com/WurthElektronik/Proteus-Windows-SDK
/// 
/// 
///
/// ****************************************************************************************************************
///
/// Please read the readme.md file included in this project before using.
/// Advanced users only;
/// technical knowledge on .NET, WPF, Windows API and Bluetooth LE technology and standard is required.
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
        /// Dictionary for fast MAC address lookup to avoid LINQ queries on every advertisement
        /// </summary>
        private Dictionary<string, pubBleScanItem> scannedItemsLookup;

        /// <summary>
        /// Bluetooth Low Energy - works with Profiles and characteristics. Those are given with unique indicators so called UUIDs
        /// The following 4 are the 128 bit UUIDs used by Proteus modules for generic data transmission.
        /// Proteus may include further standard-UUIDs (e.g. device information service and/or others).
        /// </summary>

        /// WE-ProWare, custom SPP-like V1 protocol
        public static readonly Guid PROTEUS_PrimaryService_UUID = new Guid("6E400001-C352-11E5-953D-0002A5D5C51B");
        public static readonly Guid PROTEUS_TX_CHARACTERISTIC_UUID = new Guid("6E400002-C352-11E5-953D-0002A5D5C51B");
        public static readonly Guid PROTEUS_RX_CHARACTERISTIC_UUID = new Guid("6E400003-C352-11E5-953D-0002A5D5C51B");

        /// WE-ProWare, custom SPP-like V2 protocol
        public static readonly Guid PROTEUS4_PrimaryService_UUID = new Guid("b70c0001-686c-4036-bb28-b797ae6a8d3a");
        public static readonly Guid PROTEUS4_TX_CHARACTERISTIC_UUID = new Guid("b70c0002-686c-4036-bb28-b797ae6a8d3a");
        public static readonly Guid PROTEUS4_RX_CHARACTERISTIC_UUID = new Guid("b70c0003-686c-4036-bb28-b797ae6a8d3a");

        /// <summary>
        /// This is the characteristic for sending data to the Proteus radio module
        /// For Multi-Connect applications multiple instances need to be created and maintained
        /// </summary>
        private static GattCharacteristic to_proteus = null;
        private static GattCharacteristic to_proteus4 = null;

        /// <summary>
        /// this is the characteristic for receiving data from the Proteus radio module.
        /// To achieve this notifications on this characteristic need to be enabled by the App.
        /// For Multi-Connect applications multiple instances need to be created and maintained
        /// </summary>
        private static GattCharacteristic from_proteus = null;
        private static GattCharacteristic from_proteus4 = null;


        /// <summary>
        /// this is an instance of the currently connected Bluetooth Low Energy device.
        /// For Multi-Connect applications multiple instances need to be created and maintained.
        /// </summary>
        private BluetoothLEDevice connectedBluetoothLeDevice = null;
        private GattDeviceServicesResult serviceResult = null;

        /// <summary>
        /// lockbit for received advertise events.
        /// As many events may occur during scanning (and even more during active scanning) this prevents hickups during 
        /// the update of the data collection for received advertise and beacon messages.
        /// </summary>
        private readonly object _receivedEventLock = new object();

        private bool isBleConnected = false;
        private bool isBleConnecting = false;
        private bool disconnectRequested = false;
        private bool subscribedForNotifications = false;
        private bool isSending = false;

        private bool macFilterEnabled = false;

        /// <summary>
        /// TaskCompletionSource for event-based disconnect signaling (replaces polling loop)
        /// </summary>
        private TaskCompletionSource<bool> disconnectTcs = null;

        private bool isBluetoothLEEneumeratorDeviceAvailable = false;

        /// <summary>
        /// version number of the custom spp-like radio protocol implemented in the Proteus module.
        /// Proteus-I, -II, -III and -e use V1
        /// Proteus-IV uses V2
        /// </summary>
        private uint we_SPPikeVer = 0;

        /// <summary>
        /// for "auto-linebreak" in the listbox. will be synced to actual width of the listbox.
        /// </summary>
        private int maxStringLength = 120;

        /// <summary>
        /// Check for some capabilities of the local windows system regarding Bluetooth
        /// </summary>        
        private static bool isLeSupported = ApiInformation.IsPropertyPresent("Windows.Devices.Bluetooth.BluetoothAdapter", "IsLowEnergySupported");
        private static bool isCentralRoleSupported = ApiInformation.IsPropertyPresent("Windows.Devices.Bluetooth.BluetoothAdapter", "IsCentralRoleSupported");
        private static bool isLeSecureConnectionSupported = ApiInformation.IsPropertyPresent("Windows.Devices.Bluetooth.BluetoothAdapter", "AreLowEnergySecureConnectionsSupported");

        /// <summary>
        /// Main Window Function. Called once on App Start.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_StartScan.IsEnabled = false));
            this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Pair.IsEnabled = false));

            scannedItems = new ObservableCollection<pubBleScanItem>();
            scannedItems.OrderBy(x => x.BleMacAddress);
            scannedItemsLookup = new Dictionary<string, pubBleScanItem>();
            lstNames.ItemsSource = scannedItems;

            if ((isCentralRoleSupported != true) ||
                (isLeSupported != true) ||
                (isLeSecureConnectionSupported != true))
            {
                MessageBox.Show("A must-have feature for this app is not supported by your system (at least one of: central role, Bluetooth LE, active scan). App will be closed.");
                return;
            }

            try
            {
                leWatcher = new BluetoothLEAdvertisementWatcher
                {
                    // Receive extended PDUs (Bluetooth 5+)
                    AllowExtendedAdvertisements = true,   // Extended ADV support  [1](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementwatcher?view=winrt-26100)
                    // Active scan ensures we get scan responses (secondary payloads)
                    ScanningMode = BluetoothLEScanningMode.Active   // get scan-response packets  [2](https://riptutorial.com/bluetooth/example/19735/listen-for-a-bluetooth-le-advertisement)
                };


                leWatcher.SignalStrengthFilter = new BluetoothSignalStrengthFilter();

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

            try
            {
                /* check if the registry contains a name "DriverDesc" with value "Microsoft Bluetooth LE Enumerator" which indicates a bluetooth LE compatible device available on the PC */

                /* DriverDesc.value == "Microsoft Bluetooth LE Enumerator" ??? */
                string baseKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
                string name = "DriverDesc";
                string expectedValue = "Microsoft Bluetooth LE Enumerator";
                bool found = false;

                /* scan subfolders 0000 to 0007 */
                for (int i = 0; i <= 7; i++)
                {
                    string subfolderPath = $@"{baseKeyPath}\{i:D4}";

                    try
                    {
                        using (RegistryKey keys = Registry.LocalMachine.OpenSubKey(subfolderPath, RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadKey))
                        {
                            if (keys != null)
                            {
                                object value = keys.GetValue(name);
                                if (value != null && value.ToString() == expectedValue)
                                {
                                    isBluetoothLEEneumeratorDeviceAvailable = true;
                                    found = true;
                                    Console.WriteLine($"Found Bluetooth LE Enumerator at: {subfolderPath}");
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception subfolderException)
                    {
                        /* continue checking other subfolders if one fails */
                        Console.WriteLine($"Exception when accessing registry subfolder {i:D4}: {subfolderException.Message}");
                    }
                }

                if (!found)
                {
                    isBluetoothLEEneumeratorDeviceAvailable = false;
                }

            }
            catch (Exception registryReadException)
            {
                Console.WriteLine("Exception when accessing registry LE Enumerator Name :=" + registryReadException.Message);
                isBluetoothLEEneumeratorDeviceAvailable = false;
            }

            if (isBluetoothLEEneumeratorDeviceAvailable != true)
            {
                this.addItemToListBox("Warning: The registry does not contain the required >Microsoft Bluetooth LE Enumerator< device at the expected position.");
                this.addItemToListBox("Warning: Most likely your Bluetooth function does not support the Low Energy extension.");
                this.addItemToListBox("Warning: Please check the readme.md for Bluetooth dependencies.");
            }
            else
            {
                this.addItemToListBox("Info: Found >Microsoft Bluetooth LE Enumerator< device.");
            }


            this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                this.btn_StartScan.IsEnabled = true;
                this.btn_StartScan.Background = new SolidColorBrush(Colors.LightGreen);
                this.btn_Pair.IsEnabled = true;
            }));

            this.Dispatcher.Invoke(() => macFilterEnabled = cb_ApplyMACFilter.IsChecked == true);

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

            if (bluetoothRadio == null)
            {
                this.addItemToListBox("Warning: radios.FirstOrDefault did not find any Bluetooth device.");
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
                this.addItemToListBox("Info: radios.FirstOrDefault found a 'Other' radio device.");
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
                    // Batch all UI updates into a single dispatcher call for better performance
                    this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        cb_ApplyUUIDFilter.IsEnabled = false;
                        cb_ApplyRSSIFilter.IsEnabled = false;
                        cb_ApplyMACFilter.IsEnabled = false;
                        btn_StartScan.IsEnabled = false;
                        btn_Pair.IsEnabled = false;
                        btn_StopScan.IsEnabled = true;
                        btn_StopScan.Background = new SolidColorBrush(Colors.LightGreen);
                        btn_StartScan.Background = new SolidColorBrush(SystemColors.ControlColor);
                        btn_Connect.Background = new SolidColorBrush(SystemColors.ControlColor);
                    }));

                    leAdvertiseFilter = new BluetoothLEAdvertisementFilter();
                    if (cb_ApplyUUIDFilter.IsChecked == true)
                    {
                        /* filter for PROTEUS module advertisements, only */
                        leAdvertiseFilter.Advertisement.ServiceUuids.Add(PROTEUS4_PrimaryService_UUID); /* Proteus-IV primary service */
                        leAdvertiseFilter.Advertisement.ServiceUuids.Add(PROTEUS_PrimaryService_UUID); /* Proteus primary service */
                    }
                    else
                    {
                        /* nothing to do */
                    }

                    leWatcher.SignalStrengthFilter = new BluetoothSignalStrengthFilter();
                    leWatcher.SignalStrengthFilter.SamplingInterval = TimeSpan.FromMilliseconds(250);
                    leWatcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromMilliseconds(5000);
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
                        addItemToListBox("Advertisement watcher started.");
                    }
                }
            }
            catch (Exception ex3)
            {
                addItemToListBox("Bluetooth LE Scan function could not be used.\r\n" + "ERROR:  " + ex3.Message.ToString());

                // Batch all UI updates into a single dispatcher call
                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    cb_ApplyUUIDFilter.IsEnabled = true;
                    cb_ApplyRSSIFilter.IsEnabled = true;
                    cb_ApplyMACFilter.IsEnabled = true;
                    btn_StartScan.IsEnabled = true;
                    btn_Pair.IsEnabled = true;
                    btn_StopScan.IsEnabled = false;
                }));

                addItemToListBox("\r\n" + "Enable Bluetooth function in device manager and check that a Bluetooth LE compatible device is correctly installed in your PC.");
            }
        }

        /// <summary>
        /// Called when GUI button Clear Scan is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btn_ClearScan_Click(object sender, RoutedEventArgs e)
        {
            if (leWatcher.Status != BluetoothLEAdvertisementWatcherStatus.Started)
            {
                scannedItems.Clear();
                scannedItemsLookup.Clear();
                addItemToListBox("Scan results cleared.");

                // Batch UI updates
                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    btn_Connect.IsEnabled = false;
                    btn_Pair.IsEnabled = false;
                    btn_Connect.Background = new SolidColorBrush(SystemColors.ControlColor);
                }));
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
                    // Batch UI updates
                    this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        btn_Connect.IsEnabled = true;
                        btn_Pair.IsEnabled = true;

                        if (this.scannedItems.Count == 1)
                        {
                            this.lstNames.SelectedIndex = 0;
                            btn_Connect.Background = new SolidColorBrush(Colors.LightGreen);
                            btn_StartScan.Background = new SolidColorBrush(Colors.LightGreen);
                        }
                    }));
                }
            }
        }

        private void btn_Pair_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                if (leWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                {
                    /// pairing is currently not allowed as scan is running
                }
                else
                {
                    /* no scan running, pairing process may start */
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

                    PairProteusDevice(selected_remote_mac);

                }
            }
            catch (Exception ex4)
            {
                addItemToListBox("Bluetooth LE connect could not be used.\r\n" + "ERROR:  " + ex4.Message.ToString());

                // Batch UI updates
                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    cb_ApplyUUIDFilter.IsEnabled = true;
                    cb_ApplyRSSIFilter.IsEnabled = true;
                    btn_StartScan.IsEnabled = true;
                    btn_Pair.IsEnabled = true;
                    btn_StopScan.IsEnabled = false;
                    btn_Connect.IsEnabled = false;
                    btn_Disconnect.IsEnabled = false;
                }));

                addItemToListBox("\r\n" + "Enable Bluetooth function in device manager and check that a Bluetooth LE compatible device is correctly installed in your PC.");
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

                    // Batch UI updates
                    this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        btn_Connect.Background = new SolidColorBrush(SystemColors.ControlColor);
                        btn_StartScan.Background = new SolidColorBrush(SystemColors.ControlColor);
                        btn_Connect.IsEnabled = false;
                        btn_StartScan.IsEnabled = false;
                        btn_Pair.IsEnabled = false;
                    }));

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
                addItemToListBox("Bluetooth LE connect could not be used.\r\n" + "ERROR:  " + ex4.Message.ToString());

                // Batch UI updates
                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    cb_ApplyUUIDFilter.IsEnabled = true;
                    cb_ApplyRSSIFilter.IsEnabled = true;
                    btn_StartScan.IsEnabled = true;
                    btn_Pair.IsEnabled = true;
                    btn_StopScan.IsEnabled = false;
                    btn_Connect.IsEnabled = false;
                    btn_Disconnect.IsEnabled = false;
                }));

                addItemToListBox("\r\n" + "Enable Bluetooth function in device manager and check that a Bluetooth LE compatible device is correctly installed in your PC.");
            }
        }

        /// <summary>
        /// Called when GUI button Disconnect is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btn_Disconnect_Click(object sender, RoutedEventArgs e)
        {
            /// set the flag and signal the disconnect task
            this.disconnectRequested = true;

            /// Signal the TaskCompletionSource to unblock the waiting connection thread
            disconnectTcs?.TrySetResult(true);
        }

        /// <summary>
        /// Called when GUI button Send data is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btn_Send_Click(object sender, RoutedEventArgs e)
        {
            if (isBleConnected != true)
            {
                return;
            }

            /// payload is taken from textbox (i.e. hexadecimal string)
            byte[] payload = Converters.StringToByteArray(this.tb_TxPayload.Text);

            ///StringToByteArray() will return null when: sting has not even number of characters or string is longer than max MTU for Proteus.
            if (payload == null)
            {
                addItemToListBox("TX Proteus: illegal payload. Payload must be even number of characters.");
                return;
            }

            if ((payload.Length == 0) || (payload.Length > 243))
            {
                addItemToListBox("TX Proteus: illegal payload. Payload must be between 1 and 243 bytes.");
                return;
            }

            try
            {
                /// Note: in Bluetooth Low Energy (4.0) compatibility mode: payload size is limited to 19 bytes!
                /// Bluetooth 4.2 in your PC is required for larger packets.
                SendData(payload);
            }
            catch (Exception ex5)
            {
                addItemToListBox("Bluetooth LE SendTo could not be used.\r\n" + "ERROR:  " + ex5.Message.ToString());
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
                addItemToListBox("Advertisement watcher stopped.");
            }
            else
            {
                addItemToListBox("Error: " + args.Error.ToString());
            }

            ///GIU modifications due to stopped LE advertise scanner
            // Batch UI updates
            this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                cb_ApplyUUIDFilter.IsEnabled = true;
                cb_ApplyRSSIFilter.IsEnabled = true;
                cb_ApplyMACFilter.IsEnabled = true;
                btn_StopScan.IsEnabled = false;
                btn_StartScan.IsEnabled = true;
                btn_Pair.IsEnabled = true;
            }));
        }

        /// <summary>
        /// Is triggered when a advertise packet is received.
        /// This event may be filtered by selected filters (e.g. UUID, RSSI, MAC address, Devicename, ....) and not show any advertising Bluetooth LE device.
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

                /// Apply MAC address filter if enabled - only show devices with MAC starting with 0x0018DA


                if (macFilterEnabled == true)
                {
                    /// Extract the first 3 bytes (24 bits) of the MAC address (MA-L)
                    ulong macPrefix = btAddress >> 24;
                    if (macPrefix != 0x0018DA)
                    {
                        /// MAC address does not match the filter, skip this device
                        return;
                    }
                }

                // Display local name if available
                if (!string.IsNullOrEmpty(args.Advertisement.LocalName))
                {
                    //Console.WriteLine($"Local Name: {args.Advertisement.LocalName}");
                }

                string localName = args.Advertisement.LocalName;
                if (args.IsScanResponse == true)
                {
                    /* update table even if entry does already exists */
                    localName = args.Advertisement.LocalName; /* on proteus-IV this will happen when Scan Response is received */

                }

                if (args.Advertisement.ManufacturerData.Count > 0)
                {
                    // Display manufacturer data
                    foreach (var manufacturerData in args.Advertisement.ManufacturerData)
                    {
                        var data = new byte[manufacturerData.Data.Length];
                        using (var reader = DataReader.FromBuffer(manufacturerData.Data))
                        {
                            reader.ReadBytes(data);
                        }
                        Console.WriteLine($"Manufacturer ID: 0x{manufacturerData.CompanyId:X}");
                        Console.WriteLine($"Manufacturer Data: {BitConverter.ToString(data)}");
                    }
                }

                IList<Guid> uuidList = args.Advertisement.ServiceUuids;

                /// create temporary ble scan result item
                pubBleScanItem temp = new pubBleScanItem(btAddress.ToString("X12"), localName, rssi, timestamp.ToLocalTime().ToString("HH:mm:ss.fff")) { };
                
                this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    /// an item in the collection is uniquely identifyable only using the Bluetooth MAC address.
                    if (scannedItemsLookup.TryGetValue(temp.BleMacAddress, out pubBleScanItem item))
                    {
                        /* item is already in the collection -> change it! */

                        if ((args.AdvertisementType == BluetoothLEAdvertisementType.ConnectableUndirected) || (args.AdvertisementType == BluetoothLEAdvertisementType.ScanResponse))
                        {
                            /// is the received signal strenth valid ?
                            /// discard scan results with invalid rssi.
                            if (rssi > -127)
                            {

                                item.Update(temp.Timestamp, temp.Name, temp.Rssi);

                            }
                        }
                        else if (args.AdvertisementType == BluetoothLEAdvertisementType.NonConnectableUndirected) /* beacons */
                        {

                        }
                    }
                    else
                    {
                        /* item is not yet in the collection -> add it! */

                        if (rssi > -127)
                        {
                            scannedItems.Add(temp);
                            scannedItemsLookup[temp.BleMacAddress] = temp;
                        }
                    }
                }
                ));
                
            }
            catch (NullReferenceException ex)
            {
                ex.ToString();
            }
        }

        #endregion "Bluetooth LE Advertisment related Events"

        #region "Connect to Proteus Bluetooth LE device"

        /// <summary>
        /// Connect to Proteus Bluetooth LE device
        /// </summary>
        /// <param name="mac">6 byte MAC Bluetooth LE adresse</param>
        private async void ConnectProteusDevice(ulong mac)
        {
            if ((isBleConnected == false) &&
                (isBleConnecting == false))
            {
                isBleConnected = false;
                isBleConnecting = true;

                try
                {
                    /// Note: BluetoothLEDevice.FromIdAsync must be called from a UI thread because it may prompt for consent.
                    connectedBluetoothLeDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(mac, BluetoothAddressType.Public);
                }
                catch (Exception ex)
                {
                    addItemToListBox("Info: Bluetooth seems to be disabled / off." + ex.ToString());

                    isBleConnecting = false;
                    connectedBluetoothLeDevice?.Dispose();
                    connectedBluetoothLeDevice = null;

                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Disconnect.IsEnabled = false));
                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Connect.IsEnabled = true));
                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.tb_TxPayload.Text = string.Empty));


                    return; /* do not continue with connect */
                }

                connectedBluetoothLeDevice.ConnectionStatusChanged += this.OnConnectionStatusChanged;
                connectedBluetoothLeDevice.GattServicesChanged += this.OnGattServicesChanged;


                /* this device is able to securely pair to another device */

                //connectedBluetoothLeDevice.DeviceInformation.Pairing.c

                if (connectedBluetoothLeDevice.DeviceInformation.Pairing.IsPaired == true)
                {
                    /* is already paired - needs to be achieved via Windows Bluetooth device manager "add device" functionality */
                    addItemToListBox("Info: Paired, level: " + connectedBluetoothLeDevice.DeviceInformation.Pairing.ProtectionLevel.ToString());
                }
                else
                {
                    addItemToListBox("This device is not yet paired. Plese use Windows Bluetooth device manager to perform the pairing before you try to connect.");
                    isBleConnecting = false;

                    connectedBluetoothLeDevice.ConnectionStatusChanged -= this.OnConnectionStatusChanged;
                    connectedBluetoothLeDevice.GattServicesChanged -= this.OnGattServicesChanged;
                    connectedBluetoothLeDevice?.Dispose();
                    connectedBluetoothLeDevice = null;

                    // Batch UI updates
                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        cb_ApplyUUIDFilter.IsEnabled = true;
                        cb_ApplyRSSIFilter.IsEnabled = true;
                        cb_ApplyMACFilter.IsEnabled = true;
                        btn_StopScan.IsEnabled = false;
                        btn_StartScan.IsEnabled = true;
                        btn_Pair.IsEnabled = true;
                        btn_Connect.IsEnabled = true;
                    }));

                    return;/* do not continue with connect */
                }


                #region "Get Characteristics and enable Notification"
                serviceResult = await connectedBluetoothLeDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached); /* do not use cached bt services, this may take longer but ensures correct behaviour */

                if (serviceResult.Status == GattCommunicationStatus.Success)
                {
                    foreach (var s in serviceResult.Services)
                    {
                        addItemToListBox("Found service UUID: " + s.Uuid);

                        if (s.Uuid == PROTEUS4_PrimaryService_UUID)
                        {
                            GattCharacteristicsResult characteristicResult = await s.GetCharacteristicsAsync();
                            if (characteristicResult.Status == GattCommunicationStatus.Success)
                            {
                                foreach (GattCharacteristic c in characteristicResult.Characteristics)
                                {
                                    switch (c.Uuid)
                                    {
                                        case var r when (r == PROTEUS4_TX_CHARACTERISTIC_UUID):
                                            {
                                                /// for data sending to_proteus4
                                                addItemToListBox("Found Proteus-IV TX Characteristic: " + c.Uuid);
                                                GattCharacteristicProperties properties = c.CharacteristicProperties;

                                                if (properties.HasFlag(GattCharacteristicProperties.Write))
                                                {
                                                    /// This characteristic supports writing to it.
                                                    to_proteus4 = c;

                                                    /// sending to Proteus is possible now!
                                                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Send.IsEnabled = true));
                                                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Send.Background = new SolidColorBrush(Colors.LightGreen)));
                                                }
                                                else if (properties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
                                                {
                                                    /// This characteristic supports writing to it.
                                                    to_proteus4 = c;

                                                    /// sending to Proteus is possible now!
                                                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Send.IsEnabled = true));
                                                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Send.Background = new SolidColorBrush(Colors.LightGreen)));
                                                }
                                                else
                                                {
                                                    /// error
                                                    to_proteus4 = null;
                                                }
                                            }
                                            break;
                                        case var r when (r == PROTEUS4_RX_CHARACTERISTIC_UUID):
                                            {
                                                /// for data receiving from_proteus4
                                                addItemToListBox("Found Proteus-IV RX Characteristic: " + c.Uuid);
                                                GattCharacteristicProperties properties = c.CharacteristicProperties;

                                                if (true == properties.HasFlag(GattCharacteristicProperties.Notify))
                                                {
                                                    try
                                                    {
                                                        /// This characteristic supports subscribing to notifications.
                                                        from_proteus4 = c;

                                                        /// Proteus requires Notifications enabled!
                                                        GattCommunicationStatus status = await from_proteus4.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                                                        if (status == GattCommunicationStatus.Success)
                                                        {
                                                            /// notifications were enabled, register value changed event for received data from Proteus device
                                                            subscribedForNotifications = true;
                                                            from_proteus4.ValueChanged += Characteristic_ValueChanged;
                                                            addItemToListBox("Enabled notifications for Proteus RX Characteristic.");
                                                        }
                                                        else
                                                        {
                                                            /// error: cannot enable notifications
                                                            from_proteus4 = null;
                                                        }
                                                    }
                                                    catch (Exception ex001)
                                                    {
                                                        addItemToListBox("Connected device requires security, but security connection failed.");
                                                        MessageBox.Show(ex001.Message);
                                                        from_proteus4 = null;
                                                    }
                                                }
                                                else
                                                {
                                                    /// errror: property "Notify" missing in propertys
                                                    from_proteus4 = null;
                                                }
                                            }
                                            break;
                                        default:
                                            {
                                                addItemToListBox("Found unknown/unemplemented characteristic: " + c.Uuid);
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
                        else if (s.Uuid == PROTEUS_PrimaryService_UUID)
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
                                                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Send.Background = new SolidColorBrush(Colors.LightGreen)));
                                                }
                                                else if (properties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
                                                {
                                                    /// This characteristic supports writing to it.
                                                    to_proteus = c;

                                                    /// sending to Proteus is possible now!
                                                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Send.IsEnabled = true));
                                                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Send.Background = new SolidColorBrush(Colors.LightGreen)));
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
                                                addItemToListBox("Found Proteus-IV RX Characteristic: " + c.Uuid);
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
                                                    catch (Exception ex002)
                                                    {
                                                        addItemToListBox("Connected device requires security, but security connection failed.");
                                                        MessageBox.Show(ex002.Message);
                                                        from_proteus = null;
                                                    }
                                                }
                                                else
                                                {
                                                    /// errror: property "Notify" missing in propertys
                                                    from_proteus = null;
                                                }
                                            }
                                            break;
                                        default:
                                            {
                                                addItemToListBox("Found unknown/unemplemented characteristic: " + c.Uuid);
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


                    if ((from_proteus != null) &&
                        (to_proteus != null))

                    {
                        this.we_SPPikeVer = 1;
                    }
                    else if ((from_proteus4 != null) &&
                        (to_proteus4 != null))
                    {
                        this.we_SPPikeVer = 2;
                    }
                    else
                    {
                        this.we_SPPikeVer = 0;
                    }
                #endregion



                    if (this.we_SPPikeVer == 0)
                    {
                        this.disconnectRequested = true;
                        isBleConnecting = false;
                        isBleConnected = false;
                    }
                    #region "Channel Open - comminucation with Proteus"
                    else if (we_SPPikeVer == 2)
                    {


                        /* connection success */

                        isBleConnecting = false;
                        isBleConnected = true;

                        /* detect negotiated PDU and calculate max supported user payload */
                        uint maxPduSize_ul = from_proteus4.Service.Session.MaxPduSize;
                        uint maxPduSize_dl = to_proteus4.Service.Session.MaxPduSize;
                        uint maxPduSize = (maxPduSize_ul >= maxPduSize_dl) ? maxPduSize_dl : maxPduSize_ul; /* select smaller of both - though they should be equal */
                        addItemToListBox("Info: WE-ProWare SPP-like V2 protocol detected.");
                        addItemToListBox("Info: Negotiated PDU = " + maxPduSize.ToString() + " bytes");
                        addItemToListBox("Info: Max user payload = (PDU - 4) = " + (maxPduSize - 4).ToString() + " bytes");

                        /* Display connection session information */
                        try
                        {
                            var session = from_proteus4.Service.Session;
                            if (session != null)
                            {
                                addItemToListBox("Info: Session established - Device ID: " + session.DeviceId.Id);
                                if (maxPduSize > 27)
                                {
                                    addItemToListBox("Info: Extended PDU negotiated - Windows is using BLE 4.2+ features");
                                }
                            }
                        }
                        catch (Exception sessionEx)
                        {
                            addItemToListBox("Info: Could not retrieve detailed session info: " + sessionEx.Message);
                        }

                        addItemToListBox("Indication: Channel open");

                        await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Disconnect.IsEnabled = true));
                        await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Disconnect.Background = new SolidColorBrush(Colors.LightGreen)));

                        /// Initialize TaskCompletionSource and wait for disconnect signal (replaces polling loop)
                        disconnectTcs = new TaskCompletionSource<bool>();
                        await disconnectTcs.Task;
                    }
                    else if (we_SPPikeVer == 1)
                    {


                        /* connection success */

                        isBleConnecting = false;
                        isBleConnected = true;

                        /* detect negotiated PDU and calculate max supported user payload */
                        uint maxPduSize_ul = from_proteus.Service.Session.MaxPduSize;
                        uint maxPduSize_dl = to_proteus.Service.Session.MaxPduSize;
                        uint maxPduSize = (maxPduSize_ul >= maxPduSize_dl) ? maxPduSize_dl : maxPduSize_ul; /* select smaller of both - though they should be equal */
                        addItemToListBox("Info: WE-ProWare SPP-like V1 protocol detected.");
                        addItemToListBox("Info: Negotiated PDU = " + (maxPduSize).ToString() + " -1 bytes");
                        addItemToListBox("Info: Max user payload = (PDU - 4 - 1) = " + (maxPduSize - 4 -1).ToString() + " bytes");

                        /* Display connection session information */
                        try
                        {
                            var session = from_proteus.Service.Session;
                            if (session != null)
                            {
                                addItemToListBox("Info: Session established - Device ID: " + session.DeviceId.Id);
                                if (maxPduSize > 27)
                                {
                                    addItemToListBox("Info: Extended PDU negotiated - likely using BLE 4.2+ features");
                                }
                            }
                        }
                        catch (Exception sessionEx)
                        {
                            addItemToListBox("Info: Could not retrieve detailed session info: " + sessionEx.Message);
                        }

                        addItemToListBox("Indication: Channel open");

                        await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Disconnect.IsEnabled = true));
                        await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Disconnect.Background = new SolidColorBrush(Colors.LightGreen)));

                        /// Initialize TaskCompletionSource and wait for disconnect signal (replaces polling loop)
                        disconnectTcs = new TaskCompletionSource<bool>();
                        await disconnectTcs.Task;
                    }
                    #endregion

                    #region "Disconnect"
                    if (disconnectRequested == true)
                    {
                        if (we_SPPikeVer == 2)
                        {
                            connectedBluetoothLeDevice.ConnectionStatusChanged -= this.OnConnectionStatusChanged;
                            connectedBluetoothLeDevice.GattServicesChanged -= this.OnGattServicesChanged;

                            ///
                            /// perform gentle disconnect:
                            ///1st check if notifications are still on, if so - disable notifications
                            ///
                            if (subscribedForNotifications == true)
                            {
                                try
                                {

                                    /// unregister value changed event
                                    from_proteus4.ValueChanged -= Characteristic_ValueChanged;

                                    /// disable notifications
                                    GattCommunicationStatus status = await from_proteus4.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None); /* Proteus requires Notifications enabled!" */
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
                        }
                        else if (we_SPPikeVer == 1)
                        {
                            connectedBluetoothLeDevice.ConnectionStatusChanged -= this.OnConnectionStatusChanged;
                            connectedBluetoothLeDevice.GattServicesChanged -= this.OnGattServicesChanged;

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
                        }

                        /// sending is not possible due to disconnect.
                        await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Send.Background = new SolidColorBrush(SystemColors.ControlColor)));
                        await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.btn_Send.IsEnabled = false));

                        isBleConnected = false;

                        /// Microsoft statement: For Bluetooth LE devices our APIs do not provide direct control over the connection to the device.
                        /// Instead windows bt stack will disconnect the device after a timeout (in seconds) after dispose
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
                        from_proteus4 = null;
                        to_proteus4 = null;
                        connectedBluetoothLeDevice = null;                        

                        GC.Collect();
                        GC.WaitForPendingFinalizers();                        

                        await Task.Delay(1000); /* according to microsoft: wait 1000ms for disconnect ble device happens */
                        addItemToListBox("Disconnected from Proteus.");

                        // Batch UI updates
                        await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                        {
                            this.btn_Disconnect.Background = new SolidColorBrush(SystemColors.ControlColor);
                            this.btn_Disconnect.IsEnabled = false;
                            this.tb_TxPayload.Text = string.Empty;
                            this.btn_StartScan.IsEnabled = true;
                            this.btn_StartScan.Background = new SolidColorBrush(Colors.LightGreen);
                            this.btn_Pair.IsEnabled = true;
                            this.btn_Connect.IsEnabled = true;
                            this.btn_Connect.Background = new SolidColorBrush(Colors.LightGreen);
                        }));
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
            else
            {
                addItemToListBox("Already connected or connecting to a Proteus.");

                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    this.btn_Disconnect.IsEnabled = false;
                    this.btn_Connect.IsEnabled = true;
                    this.btn_Pair.IsEnabled = true;
                }));
            }
        }


        private async void PairProteusDevice(ulong mac)
        {
            /* note: this method was verified with the factory default settings for security of all proteus, non default proteus settings may require custom pairing method */
            try
            {
                addItemToListBox("Attempting to pair with device: " + mac.ToString("X12"));

                var device = await BluetoothLEDevice.FromBluetoothAddressAsync(mac, BluetoothAddressType.Public);

                if (device != null)
                {
                    if (device.DeviceInformation.Pairing.IsPaired == true)
                    {
                        addItemToListBox("Device is already paired. Please use connect button to connect to the device.");
                    }
                    else if (device.DeviceInformation.Pairing.CanPair == true)
                    {
                        addItemToListBox("Starting pairing process...");
                        var pairingResult = await device.DeviceInformation.Pairing.PairAsync();

                        if (pairingResult.Status == DevicePairingResultStatus.Paired)
                        {
                            addItemToListBox("Device paired successfully.");

                            await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                            {
                                this.btn_Connect.IsEnabled = true;
                                this.btn_Connect.Background = new SolidColorBrush(Colors.LightGreen);
                            }));

                        }
                        else
                        {
                            addItemToListBox("Failed to pair device: status=" + pairingResult.Status.ToString());
                            addItemToListBox("Please ensure Pairing/Bonding information was cleared on Proteus device!");
                        }
                    }
                    else if (device.DeviceInformation.Pairing.CanPair == false)
                    {
                        addItemToListBox("Device does not support pairing. Try use connect button to connect to the device.");
                    }

                    device.Dispose();
                }
                else
                {
                    addItemToListBox("Failed to get BluetoothLE Device from MAC address.");
                }
            }
            catch (Exception ex)
            {
                addItemToListBox("Error during pairing attempt: " + ex.Message);
            }
        }

        #endregion

        #region "Events: while connected"
        private void OnGattServicesChanged(BluetoothLEDevice sender, object args)
        {
            /// currently unused
            addItemToListBox("Detected GATT Service change event on active connection.");
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

        #region "Send data to Proteus Bluetooth LE device"

        /// <summary>
        /// Send payload to Proteus device.
        /// Using the Proteus Header
        /// </summary>
        /// <param name="payload">user payload byte array</param>
        private async void SendData(byte[] payload)
        {
            /// check if characteristic is avialble and ble device is connected
            if ((isBleConnected != true) || (we_SPPikeVer == 0))
            {
                return;
            }

            /// prevent multiple concurrent sends
            if (isSending)
            {
                //addItemToListBox("TX busy - previous transmission still in progress.");
                return;
            }

            try
            {
                isSending = true;

                /// disable send button while transmitting
                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    this.btn_Send.IsEnabled = false;
                    this.btn_Send.Background = new SolidColorBrush(SystemColors.ControlColor);
                }));

                await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => this.tb_TxPayload.Text = string.Empty));

                var writer = new DataWriter();
                byte[] toSend = new byte[payload.Length];
                Array.ConstrainedCopy(payload, 0, toSend, 0, payload.Length);
                writer.WriteBytes(toSend);

                if (we_SPPikeVer == 1)
                {

                    toSend = new byte[payload.Length + 1];

                    toSend[0] = 0x01; /* add header "Proteus spplike v1" before payload data" */
                    Array.ConstrainedCopy(payload, 0, toSend, 1, payload.Length);
                    writer.WriteBytes(toSend);


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
                else if (we_SPPikeVer == 2)
                {
                    GattCommunicationStatus result = await to_proteus4.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse); /* WriteWithoutResponse will result true if data was sent to the controller, but no ack from device is expected. */
                    if (result == GattCommunicationStatus.Success)
                    {
                        // Successfully wrote to device
                        addItemToListBox("TX Proteus-IV payload data: 0x" + Converters.ByteArrayToString(toSend.ToArray())); /* first byte of payload is representing command byte header of the protocol used in proteus. - here: 0x01 for "data" */
                    }
                    else
                    {
                        addItemToListBox("TX Proteus-IV payload data failed.");
                    }
                }
            }
            catch (Exception ex)
            {
                addItemToListBox("TX Proteus-IV payload data failed." + ex.ToString());
            }
            finally
            {
                /// always reset flag and re-enable button
                isSending = false;

                /// re-enable send button if still connected
                if (isBleConnected)
                {
                    await this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        this.btn_Send.IsEnabled = true;
                        this.btn_Send.Background = new SolidColorBrush(Colors.LightGreen);
                    }));
                }
            }

        }


        #endregion "Send data to Proteus Bluetooth LE device"

        #region "Event: Received data from Proteus Bluetooth LE device"

        /// <summary>
        /// ValueChanged event handler for connected Bluetooth LE device. aka: "receive data" in Bluetooth LE terminology.
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
            if (we_SPPikeVer == 2)
            {
                /* gui output slows down throughput, only enabled in not rx throughput measurements */
                addItemToListBox("RX Proteus-IV payload data: 0x" + Converters.ByteArrayToString(output.ToArray()));
            }
            else if (we_SPPikeVer == 1)
            {
                /* gui output slows down throughput, only enabled in not rx throughput measurements */
                addItemToListBox("RX Proteus payload data: 0x" + Converters.ByteArrayToString(output.Skip(1).ToArray()));
            }
        }


        #endregion "Received data from Proteus Bluetooth LE device"


    }
}