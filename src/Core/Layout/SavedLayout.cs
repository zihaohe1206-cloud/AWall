using System;
using System.Collections.Generic;

namespace AWall {
// %LOCALAPPDATA%\AWall\wallpaper-layout.json (wallpaper-layout-test.json in test runs).
// Title..Preview is the primary display's wallpaper (also what older builds read); Screens is per display.
public sealed class SavedScreen {public string Device,Title,Type,File,Preview;}
public sealed class SavedLayout {public string Title,Type,File,Preview;public bool ManualPause;public bool PauseCovered=true;public List<SavedScreen> Screens;}
}
