# Ratings

## ChroMapper plugin

Display BeatLeader Pass, Tech, Acc and Star rating for the full map and for an average of notes while mapping  
Also display Linear %, Multi-Notes rating, Peak Sustained EBPM, Crouch Wall Action, Dodge Wall Action and Bomb Avoidance count  

### How to install
Download the latest release here: https://github.com/Loloppe/ChroMapper_Ratings/releases/latest  

All:  
Make sure that Ratings.dll and model_sleep_bl.onnx are in your main Plugins folder.  

Windows:  
Extract onnxruntime.dll in the ChroMapper_Data\Plugins\x86_64 folder.  

Mac:  
CM.app -> Right click -> Show package contents  
Extract libonnxruntime.dylib (x64 or arm64) inside Contents\Plugins.  
  
Linux:  
Extract libonnxruntime.so in the ChroMapper_Data\Plugins folder.  

### How to use

This plugin with load automatically on map load and run automatically  
You can press Tab while mapping, a Ratings text icon should appear on the menu on the right side of the screen  
Clicking that icon will open the Ratings menu  
If you want to reload the map, first save the map, then press the Reload Map button  

### Options

- Enabled : Can be used to hide the UI and text  
- Timescale : Simulate a different speed for the map (SS: 0.85, FS: 1.2, SFS: 1.5). You must Reload Map for this to apply  
- Note Count : Change how many notes to uses from cursor and onward for the average
- Star Calc : Change the % accuracy used to calculate the star rating (default 0.96)
