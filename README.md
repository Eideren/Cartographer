A tool to convert UDK maps to UE4, it only converts the map itself, assets must already exist within the UE4 project for this tool to assign them properly.

### Usage
1. Place the executable at the root of your UE4 project
2. In UDK with your map open, `Edit>Select All`, `Edit>Copy`. This will serialize the map into your clipboard.
3. Launch the executable, it'll read your clipboard, convert the content and  write back to it.
4. In UE4, `Edit>Paste`

### Options
The executable reads up to two arguments, the first one is an input file if you don't want to work off of the clipboard and the second one is the UE4 project location.

### Building
1. Install dotnet 6 (or later) sdk
2. Open a command line in that dir and type in ``dotnet publish``
