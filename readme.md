# Standoff 2 Skin Changer (PC)

A lightweight C# memory management utility for Standoff 2 running on PC emulators.

---

## Prerequisites

* **Operating System**: Windows 10 / 11 (x64)
* **Framework**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* **Emulator**: LDPlayer, or NoxPlayer (Running Standoff 2) Blue stack dosent work beacuse it have stronger anti read memory protection

---

## Installation & Building

1. **Clone the Repository**:
   ```bash
   git clone https://github.com/uxxx00/SO2SKINCANGER.git
   cd SO2SKINCANGER
   ```

2. **Build the Project**:
   ```bash
   dotnet build -c Release -r win-x64 --self-contained false
   ```

3. **Locate Build Output**:
   Navigate to the build directory in File Explorer:
   ```C:\Users\Admin\New folder (2)\bin\Release\net8.0\win-x64
   ```

---

## Usage Instructions

1. Ensure **`skins.txt`** is placed in the same directory as `SkinChanger.exe`.
2. Launch your emulator and start **Standoff 2**.
3. Right-click **`SkinChanger.exe`** and select **Run as Administrator**.
4. Follow the on-screen menu to select or input your target skin IDs.

---

## Notes

* **Administrator Privileges**: Required for Windows memory query operations.
* **Skin IDs**: Ensure accurate skin IDs are used from `skins.txt` (e.g., specific Tec-9 or knife skin IDs).
