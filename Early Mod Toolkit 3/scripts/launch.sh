#!/usr/bin/env bash

dotnet build --configuration Debug --runtime linux-x64 --no-self-contained
./bin/Debug/net10.0/linux-x64/EarlyModToolKit
