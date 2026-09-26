// ControlPanel.h: Battery-level named-pipe server

#pragma once

EXTERN_C_START

_IRQL_requires_(PASSIVE_LEVEL)
NTSTATUS
AmtPtpControlPanelPipeStart(
	_In_ PDEVICE_CONTEXT DeviceContext
);

_IRQL_requires_(PASSIVE_LEVEL)
VOID
AmtPtpControlPanelPipeStop(
	_In_ PDEVICE_CONTEXT DeviceContext
);

_IRQL_requires_(PASSIVE_LEVEL)
NTSTATUS
AmtPtpReadBatteryLevel(
	_In_  PDEVICE_CONTEXT DeviceContext,
	_Out_ UCHAR* BatteryLevel
);

EXTERN_C_END
