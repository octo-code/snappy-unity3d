snappy-unity3d
==============

This is a unity3d targeted version of snappy, google's fast compressor. This is based on the following two projects:
* https://code.google.com/p/snappy/
* http://snappy.angeloflogic.com/

This will require Unit3d Pro Licensing for Desktop as this uses the 'Native Code Plugins Support' Feature, see here http://unity3d.com/unity/licenses.

Project Overview
----------------

The 'unity' folder contains a Unity 4.3 project that should run an example of snappy in unity3d straight away with no work neccessary. If you want to embed snappy into our project it should be as simple as copying it from here.

The 'snappy.net' folder contains a C# .net 3.5 Wrapper (note this will eventually be a mono .net 2.0 if possible, to ensure best unity3d compatiblity). This folder's project is VS 2012, hence VS 2012+ will be needed if you wish to alter any of the wrapper code.

The 'snappy' folder contains googles C/C++ code for snappy itself. Also within this folder sits build scripts for android, which will require android-ndk (note I'm using ndk r9), this will be the native code for android arm7. There is also a Visual Studio 2008 Project that is for compiling a windows desktop version of snappy. There is also a WSA VS 2012 project for compiling snappy for both Window 8/8.1 Metro ARM and x86. The Xcode3 project is for targeting OSX however this is not yet finished.

For iOS compiling of snappy, there is a Post-Build event within the unity example that injects the snappy files into the xcode project which unity3d will generate upon attempting an iOS build from unity.

Platform Support
----------------

Windows Desktop x86     -   Functional
Android ARM             -   Functional
Metro x86               -   Untested
Metro ARM               -   Untested
iOS                     -   Untested
Blackberry              -   Not Implemented
OSX                     -   Not Implemented
Linux                   -   Not Implemented
NaCL                    -   Not Implemented
WebPlayer               -   Not Possible

Support
----------------

Only built and tested for Unity 4.3.4f1.
