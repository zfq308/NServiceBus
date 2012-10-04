param($installPath, $toolsPath, $package, $project)

#default to build dir when debugging
if(!$toolsPath){
	$toolsPath = $pwd.path + "\..\..\..\build\nservicebus.core\"
}

#Import the nservicebus ps-commandlets
Import-Module (Join-Path $toolsPath nservicebus.powershell.dll)


$nserviceBusKeyPath =  "HKCU:SOFTWARE\NServiceBus" 
$machinePrepared = $false

#Figure out if this machine is properly setup
$a = get-itemproperty -path $nserviceBusKeyPath -ErrorAction silentlycontinue
$preparedInVersion  = $a.psobject.properties | ?{ $_.Name -eq "MachinePreparedByVersion" }
$dontCheckMachineSetup  = $a.psobject.properties | ?{ $_.Name -eq "DontCheckMachineSetup" }

if($preparedInVersion.value){
	$machinePrepared = $true
}
  
if($machinePrepared -or $dontCheckMachineSetup.value)
{
	exit
}

$perfCountersInstalled = Install-PerformanceCounters -WhatIf
$msmqInstalled = Install-Msmq -WhatIf
$dtcInstalled = Install-Dtc -WhatIf
$ravenDBInstalled = Install-RavenDB -WhatIf

if(!$perfCountersInstalled){
	"Performance counters needs to be setup"
}

if(!$msmqInstalled){
	"Msmq needs to be setup"
}
if(!$dtcInstalled){
	"DTC needs to be setup"
}
if(!$ravenDBInstalled){
	"RavenDB needs to be setup"
}

if($perfCountersInstalled -and $msmqInstalled -and $dtcInstalled -and $ravenDBInstalled){
	"Required infrastructure is all setup no need to continue"

	#todo - set the machine is prepared flag in the registry
	exit
}
$formsPath = Join-Path $toolsPath nservicebus.forms.dll
[void] [System.Reflection.Assembly]::LoadFile($formsPath) 

$prepareMachineDialog = New-Object NServiceBus.Forms.PrepareMachine

[void] $prepareMachineDialog.ShowDialog()


if($prepareMachineDialog.AllowPrepare){
	if(!$perfCountersInstalled){
		Install-PerformanceCounters
	}
	if(!$msmqInstalled){
		$success = Install-Msmq
		if(!$success){
			$confirmReinstallOfMsmq = New-Object NServiceBus.Forms.Confirm

			$confirmReinstallOfMsmq.DialogText = "NServiceBus needs to reinstall Msmq on this machine. This will cause all local queues to be deleted. Do you want to proceed?"

			if($confirmReinstallOfMsmq.Ok){
				Install-Msmq -Force
			}
		}
	}
	if(!$dtcInstalled){
		Install-Dtc
	}
	if(!$ravenDBInstalled){
		Install-RavenDB
	}
}

if($prepareMachineDialog.DontBotherMeAgain){

	New-Item -Path $nserviceBusKeyPath -Force
	New-ItemProperty -Path $nserviceBusKeyPath -Name "DontCheckMachineSetup" -PropertyType String -Value "true" -Force
}
	