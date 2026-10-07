LED MENU CONTROL
================

To start:  double-click  LedMenu.App.exe  in this folder.
Nothing needs to be installed. You do not need .NET, an installer, or an internet connection.

To learn how to use it:  open  OPERATOR_GUIDE.html  (double-click; it opens in your web browser,
and prints cleanly if you want a paper copy at the event).

The three things to remember during an event
--------------------------------------------
  Ctrl+Shift+B     BLACKOUT on / off  (the LED goes pure black and comes back; nothing is lost)
  Ctrl+Shift+I     Identify screens   (shows each screen's number for 15 seconds)
  Ctrl+Shift+F12   STOP OUTPUT        (closes the LED window; use this if anything looks wrong)

These work even when another program is in front, as long as output is running.

Where your menus are kept
-------------------------
  C:\Users\<you>\AppData\Roaming\LedMenu
(Paste  %APPDATA%\LedMenu  into the Windows Explorer address bar to open it.)
That folder holds your menus, screen layout, settings, logo images, automatic backups and the log.
To move to another PC or to back up: copy that whole folder. The program folder you are reading now
can be deleted and replaced with a newer one without touching your menus.

If something goes wrong
-----------------------
  - The program writes a log:  %APPDATA%\LedMenu\logs   (one file per day).
  - If a data file is ever damaged the program restores the latest good backup by itself and tells you;
    the damaged file is kept next to it, never deleted.
  - The "samples" folder has a placeholder logo you can use to try things out.

Fonts: this program ships the Lato font (SIL Open Font License; see Fonts\README-FONTS.txt).
