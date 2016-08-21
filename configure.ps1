# Variables
$projectDir = Get-Location
$outdir = "${env:ProgramFiles(x86)}\Microsoft SDKs\NuGetPackages\"

# Build azure-amqp
sl Dependencies\azure-amqp\Microsoft.Azure.Amqp\
dotnet restore
dotnet build
dotnet pack
sl $projectDir

# Install azure-amqp
cp Dependencies\azure-amqp\Microsoft.Azure.Amqp\bin\Debug\Microsoft.Azure.Amqp.2.0.0.nupkg $outdir
cp Dependencies\azure-amqp\Microsoft.Azure.Amqp\bin\Debug\Microsoft.Azure.Amqp.2.0.0.symbols.nupkg $outdir

# Build azure-event-hubs
## Build Microsoft.Azure.EventHubs
sl Dependencies\azure-event-hubs\csharp\src\Microsoft.Azure.EventHubs
dotnet restore
dotnet build
dotnet pack

## Install Microsoft.Azure.EventHubs
sl bin\Debug
cp Microsoft.Azure.EventHubs.1.0.0.nupkg $outdir
cp Microsoft.Azure.EventHubs.1.0.0.symbols.nupkg $outdir
sl ..\..

## Build Microsoft.Azure.EventHubs.Processor
sl ..\Microsoft.Azure.EventHubs.Processor
dotnet restore
dotnet build
dotnet pack

## Install Microsoft.Azure.EventHubs.Processor
sl bin\Debug
cp Microsoft.Azure.EventHubs.Processor.1.0.0.nupkg $outdir
cp Microsoft.Azure.EventHubs.Processor.1.0.0.symbols.nupkg $outdir

sl $projectDir
