# This scripts combines all the files necessary to run pass-winmenu as a standalone program,
# and can package them into a ZIP file as well.

param(
# Re-copy all dependencies and libraries to the package directory 
# (normally they are maintained between builds).
	[switch]$Clean,
# Combine all required files into a ZIP file.
	[switch]$Package,
# Compress the resulting zip file. Only makes sense with -Package.
	[switch]$Compress,
# Include a standalone version of GPG in the dependencies.
	[switch]$WithGpg
)

$PKGDIR="bin/Release-Packaged"
$INCLUDEDIR="$PKGDIR/lib"
$ZIPDIR="bin/"
$ZIPNAME="pass-winmenu.zip"

if($Clean){
	if(Test-Path "$PKGDIR"){
		rm -recurse "$PKGDIR"
	}
	mkdir "$PKGDIR"
}else{
	if(Test-Path "$PKGDIR/pass-winmenu.exe"){
		rm "$PKGDIR/pass-winmenu.exe"
	}
	if(Test-Path "$PKGDIR/pass-winmenu.yaml"){
		rm "$PKGDIR/pass-winmenu.yaml"
	}
}

cp -recurse "bin/Release/lib" "$PKGDIR/lib"
# Linux and OSX are not supported, so their libraries do not have to be included.
rm -recurse "$PKGDIR/lib/osx"
rm -recurse "$PKGDIR/lib/linux"
# The PDB files aren't used either, so they can be removed as well.
rm -recurse "$PKGDIR/lib/win32/x64/*.pdb"
rm -recurse "$PKGDIR/lib/win32/x86/*.pdb"

cp "bin/Release/pass-winmenu.exe" "$PKGDIR/pass-winmenu.exe"

if($WithGpg){
	tools/7za.exe x -aos "include/GnuPG.zip" "-o$INCLUDEDIR"
	cp "embedded/default-config.yaml" "$PKGDIR/pass-winmenu.yaml"
	tools/patch.exe "$PKGDIR/pass-winmenu.yaml" "include/packaged-config.patch"
}else{
	$ZIPNAME="pass-winmenu-nogpg.zip"
	cp "embedded/default-config.yaml" "$PKGDIR/pass-winmenu.yaml"
	tools/patch.exe "$PKGDIR/pass-winmenu.yaml" "include/packaged-config-nogpg.patch"
}

if($Package){
	$ZIPPATH = "$ZIPDIR$ZIPNAME"
	if(Test-Path "$ZIPPATH"){
		echo "Removing old package: $ZIPPATH"
		rm "$ZIPPATH"
	}
	$STARTDIR=$PWD
	cd $ZIPDIR
	if($Compress){
		# These options seem to result in the smallest file size.
		../tools/7za.exe a -mm=Deflate -mfb=258 -mpass=15 "$ZIPNAME" "Release-Packaged/*"
	}else{
		../tools/7za.exe a "$ZIPNAME" "Release-Packaged/*"
	}
	../tools/7za.exe rn "$ZIPNAME" "Release-Packaged" "pass-winmenu"
	cd $STARTDIR
}
