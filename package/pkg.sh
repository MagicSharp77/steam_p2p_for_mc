#!/bin/bash
OUT_LINUX="./linux"
OUT_MAC="./mac"
OUT_WIN="./win"
OUT_ZIP="./zip"

# 2. 清理旧文件 (推荐直接删目录，比删单个文件更稳，防止有残留垃圾)
echo "🧹 Cleaning up old files..."
rm -f "$OUT_LINUX/steam_p2p_for_mc"
rm -f "$OUT_LINUX/steam_p2p_for_mc.*"
rm -f "$OUT_MAC/steam_p2p_for_mc"
rm -f "$OUT_MAC/steam_p2p_for_mc.*"
rm -f "$OUT_WIN/steam_p2p_for_mc.*"

rm -rf "$OUT_ZIP/"

echo "🚀 Packing for Linux..."
cd ..
cd src/steam_p2p_for_mc
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o  "../../package/linux"
echo "🚀 Packing for macOS..."
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -o "../../package/mac"
echo "🚀 Packing for Windows..."
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true  -o "../../package/win"  

# 3. 压缩文件
# compress

cd ..
cd ..
cd package/
zip -r linux.zip "$OUT_LINUX"/*
zip -r mac.zip "$OUT_MAC"/*
zip -r win.zip "$OUT_WIN"/*

mkdir -p "$OUT_ZIP"
mv linux.zip "$OUT_ZIP" &&
mv mac.zip "$OUT_ZIP" &&
mv win.zip "$OUT_ZIP"

