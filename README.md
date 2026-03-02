# Wuerth Elektronik eiSos GmbH & Co. KG - Demo App for Proteus, Windows
(C) Copyright 2026

License Terms:
[license_terms_SDK_Proteus_Win.pdf](https://github.com/WurthElektronik/Proteus-Windows-SDK/blob/master/license_terms_SDK_Proteus_Win.pdf) 

Sources:  
[Würth Elektronik GitHub](https://github.com/orgs/WurthElektronik/) 

## Changenotes 1.1.0 -> 1.2.0
* minimum system requirement was raised to Windows 11 24H2 
* .NET 9.0 with Visual Studio 2026
* removed complexity to streamline user experience 
* switch over to use windows (default) BT pairing instead of custom pairing
* added support for protocol (SPP-like V2) and default UUIDs of Proteus-IV
* added MAC filter (MA-L 0018DA)
* adopted license to new design and updated repo name
* app now uses "Active Scan" to request Scan Response packets from Advertisers that support this feature


- - - -

This demo programm (App SDK) uses integrated Windows 11 runtime functions to access, connect and exchange data with Proteus radio modules. 
It deliberately chooses not to use any 3rd party software that is not available for free to the public, even if it is known that Windows runtime functions does only provide rudimenary functionality.

The Demo uses C# and WPF in combination with .NET 9.0.


#### This App is no "driver" for supporting a Bluetooth Classic like SPP profile to COM port! It is a DEMO application showing the use of proteus modules with their RX and TX profiles for generic data communication with Microsoft Windows 10 and newer.


This App SDK implements the required methods to:
* Scan for devices, including filters (UUID, RSSI, MA-L) and listing of scan results
* Connect to one of the found and supported "Proteus" devices
* Enable notifications in case of a connection (required for communication direction from Proteus to this App)
* Exchange data with a Proteus device using the Proteus's integrated charactersitics for data exchange (RX and TX charactersitic).
* Perform pairing and bonding towards Proteus radio modules with default security settings (custom passkey, LESC_JustWorks)
* Detect the max. user payload that can be used for the active connection
* Support Protocol and default UUIDs of Proteus-I, -II, -III, -e and Proteus-IV

Known Limitations of Windows 11 Runtime API for Bluetooth Low Energy:
* An API to perform a clean + instantaneous disconnect is not available. Disconnect on Windows side is performed by letting the connection time-out.
* An API to trigger the change of the Bluetooth LE PHY layer (LE coded or 2MBit) is not existing. However when the module uses this function and the PC bluetooth chipset supports it (i.e. Bluetooth 6.0 or newer) the 2Mbit PHY can be used.
* An API "OnPhyUpdate" is not existing, so the App can be notified in case of an update when triggered by the proteus.
* An API "GetBluetoothVersion" is not existing. So users cannot check for the available Bluetooth (LE) Version programmatically when using the Windows RT API for Bluetooth.
* Windows API does not support pairing without bonding. This means when the Proteus RF_SecFlags setting is 0x03 the connection to the windows PC will fail (the in app pairing will not work: error - handler not registered), but when bonding is enabled in addtion to static passkey (i.e. RF_SecFlags 0x0B, as seen in Peripheral only mode per default), the in-app pairing is working. 
* Windows API does currently not provide access to trigger any LESC (LE Secure Connection) functions and methods
* Windows API does not support sending of multiple packets per connection intervall. The maximum end-to-end throughput (of user payload) is expected to show this limitation (i.e.: 1 frame of 243 byte user payload per 7.5 ms when using BLE 4.2 or newer in the PC). However reception of multiple packets per intervall seems to be working.
* Windows Bonding information (in the Windows Bluetooth manager) cannot be removed programmatically via API

#### Caution: Bluetooth Version 3.5 or older does not include Bluetooth Low Energy standard yet. Hence it does not support communication with any BLE device as present with the Proteus product family. Further: Bluetooth 4.1 and 4.0 only support 19 byte of user payload as defined in the Standard. Bluetooth 4.2 and newer may support larger user payload and per packet but this support is optional according to the Bluetooth Standard. To use features available in the newest Proteus radio module, the PC or Laptop shall use at least the same Bluetooth version as the Proteus intended to be used.



## Requirements (for compiling and running)

* Windows 11 24H2 (or newer)
* Visual Studio 2026 (18.3.0 or newer)
* .NET 9.0
* NuGet dependency: Microsoft.Windows.Compatibility and Microsoft.Xaml.Behaviors.Wpf
* A Windows 11 compatible Bluetooth Low Energy Hardware installed on the PC. Recommended: Bluetooth 6.0 or newer. This device will show up in the device manager as "Generic Bluetooth Radio" and as "Microsoft Bluetooth LE Enumerator".



- - - -

References:
[Würth Elektronik Wireless Connectivity & Sensors Documentation](https://www.we-online.com/de/components/products/wco/wireless-connectivity_1/wpan_wlan/bluetooth/articles)

App Note: "Proteus Advanced User Guide" - ANR002, ANR005 or ANR009

App Note: "Proteus Peripheral only mode" - ANR004

# Notes:

The filters implemented apply to all Advertise packets (includding extended Advertising and Scan Response packets), however not all packets may have all information present that are required for passing the filter successfully.

#### Caution: If re-pairing / re-bonding is required the existing bonding information must be cleared on both devices, that is from the Proteus radio module and from the Windows BT Settings.

The dependency for an installed Windows SDK were replaced by NuGet dependencies to Microsoft.Windows.SDK.Contracts and Microsoft.Windows.Compatibility where applicable.
NuGet will automatically install this dependencies when opening the project for the first time. This requires internet access for Nuget when called by Visual Studio or a manual user action to install the packages.

To make this programm compile in Visual Studio, installing .NET SDK 9.0 is required. Download here (or use the Visual Studio Installer): https://dotnet.microsoft.com/download


#### Caution: Bluetooth maximum transfer unit (MTU) and physical data unit (PDU) is not equal to the maximum possible radio packet size or user payload size per radio packet. Large packets may casue fragmentation. Depending on the integrated Bluetooth device Windows may or may not support using of the full 243/244 byte user payload. Windows may also apply fragmentation, switch to 2MBit PHY, restrict packet sizes or Bluetooth features.


Check which Bluetooth version is available in my PC: https://www.thewindowsclub.com/how-to-check-bluetooth-version-in-windows-10


- - - -

