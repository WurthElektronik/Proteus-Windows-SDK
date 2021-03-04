# Wuerth Elektronik eiSos GmbH & Co. KG - Demo App for Proteus, Windows 10
(C) Copyright 2020

License Terms:  
[license_terms_SDK_Proteus_Win10.pdf](https://github.com/WurthElektronik/Proteus-Windows-SDK/blob/master/license_terms_SDK_Proteus_Win10.pdf) 

Sources:  
[Würth Elektronik GitHub](https://github.com/orgs/WurthElektronik/) 


- - - -

This demo programm (App SDK) uses integrated Windows 10 runtime functions to access, connect and exchange data with Proteus radio modules. 
It deliberately chooses not to use any 3rd party software that is not available for free to the public, even if it is known that Windows runtime functions does only provide rudimenary functionality.

The Demo uses C# and WPF in combination with .NET Core 3.1.

#### This App is no "driver" for supporting a Bluetooth Classic like SPP profile to COM port! It is a DEMO application showing the use of proteus modules with their RX and TX profiles for generic data communication with Microsoft Windows 10.


This App SDK implements the required methods to:
* Scan for devices, including filters (UUID, RSSI) and listing of scan results
* Connect to one of the found devices
* Enable notifications in case of a connection (required for communication direction from Proteus to this App)
* Exchange data with a Proteus device using the Proteus's integrated charactersitics for data exchange (RX and TX charactersitic).
* Perform in-App bonding + pairing during a connection according to Proteus "bonding + static passkey" (e.g. used in peripheral only mode) and "bonding + just works" security method using the proteus default key.
* Perform in-App unbonding / unpairing of a connected device during disconnect.
* Detect the max. user payload that can be used for the active connection

Known Limitations of Windows 10 Runtime API for Bluetooth Low Energy:
* An API to perform a clean + instantaneous disconnect is not available. Disconnect on Windows side is performed by letting the connection time-out.
* An API to trigger the change of the Bluetooth LE PHY layer (LE coded or 2MBit) is not existing. However when the module uses this function and the PC bluetooth chipset supports it (i.e. Bluetooth 5.0 or newer) the 2Mbit PHY can be used.
* An API "OnPhyUpdate" is not existing, so the App can be notified in case of an update when triggered by the proteus.
* An API "GetBluetoothVersion" is not existing. So users cannot check for the available Bluetooth (LE) Version programmatically when using the Windows RT API for Bluetooth.
* Windows API does not support pairing without bonding. This means when the Proteus RF_SecFlags setting is 0x03 the connection to the windows PC will fail (the in app pairing will not work: error - handler not registered), but when bonding is enabled in addtion to static passkey (i.e. RF_SecFlags 0x0B, as seen in Peripheral only mode per default), the in-app pairing is working. 
* Windows API does currently not provide access to trigger any LESC (LE Secure Connection) functions and methods
* Windows API does not support sending of multiple packets per connection intervall. The maximum end-to-end throughput (of user payload) is expected to show this limitation (i.e.: 1 frame of 243 byte user payload per 7.5ms when using BLE 4.2 or newer in the PC). However reception of multiple packets per intervall seems to be working.
* Windows Bonding information (in the Windows Bluetooth manager) cannot be removed programmatically via API

#### Caution: Bluetooth Version 3.5 or older does not include Bluetooth Low Energy standard yet. Hence it does not support communication with any BLE device as present with the Proteus product family. Further: Bluetooth 4.1 and 4.0 only support 19 byte of user payload as defined in the Standard. Bluetooth 4.2 and newer may support larger user payload per packet buth this support is optional according to the Bluetooth Standard. 


## Requirements (for compiling)

* Windows 10, Build 1809 or newer.
* Visual Studio 2019
* .NET core 3.1 SDK or newer.
* NuGet dependency: Microsoft.Windows.SDK.Contracts.
* NuGet dependency: Microsoft.Windows.Compatibility.
* .NET framework 4.7 or newer.
* A Windows 10 compatible Bluetooth Low Energy Hardware available to the PC (recommended: Bluetooth 4.2 or newer). This device will show up in the device manager as "Generic Bluetooth Radio" and as "Microsoft Bluetooth LE Enumerator".


## Requirements (for running the published packet)

* Windows 10, Build 1809 or newer.
* .NET core 3.1 runtime or newer.
* .NET framework 4.7 runtime or newer.
* A Windows 10 compatible Bluetooth Low Energy Hardware available to the PC (recommended: Bluetooth 4.2 or newer). This device will show up in the device manager as "Generic Bluetooth Radio" and as "Microsoft Bluetooth LE Enumerator".


- - - -

References:
[Würth Elektronik Wireless Connectivity & Sensors Documentation](https://www.we-online.de/web/en/electronic_components/produkte_pb/service_pbs/wco/handbuecher/wco_handbuecher.php)

App Note: "Proteus Advanced User Guide" - ANR002, ANR005 or ANR009

App Note: "Proteus Peripheral only mode" - ANR004

App Note: "Proteus High Throughput mode" - ANR006

# Using the Windows 10 Runtime & SDK, Notes:

The dependency for an installed Windows 10 SDK were replaced by NuGet dependencies to Microsoft.Windows.SDK.Contracts and Microsoft.Windows.Compatibility.
NuGet will automatically install this dependencies when opening the project for the first time. This requires internet access for Nuget when called by Visual Studio or a manual user action to install the packages.

To make this programm compile in Visual Studio, installing .NET Core SDK 3.1 is required. Download here (or use the Visual Studio Installer): https://dotnet.microsoft.com/download


#### Caution: Bluetooth maximum transfer unit (MTU) and physical data unit (PDU) is not equal to the maximum possible radio packet size or user payload size per radio packet. Large packets may casue fragmentation. Due to protocol overheads a max. user payload of 243 byte per radio frame is supported by Proteus. Depending on the integrated Bluetooth device Windows may or may not support using of the full 243 byte user payload. Windows may also apply fragmentation if the available Bleutooth LE hardware does not support large radio frames. 


Check which Bluetooth version is available in my PC: https://www.thewindowsclub.com/how-to-check-bluetooth-version-in-windows-10

Recommended publish options can be found in the screenshot publish_options.jpg, select "File/Folder" twice on first start, then change the "Target runtime" item accordingly to the screenshot and/or your specific requirements. The screenshot shows the publish options that were used for generating the file in the publish folder in this repository:

![publish options](https://github.com/WurthElektronik/Proteus-Windows-SDK/blob/master/publish_options.jpg?raw=true)

- - - -

