// MagicTrackpad2Haptics.h: Windows haptic touchpad support for the Magic Trackpad 2.
//
// Shared by both drivers (AmtPtpHidFilter, kernel mode, Bluetooth; AmtPtpDeviceUsbUm, user mode, USB).
// Header-only C with no WDF, WPP or Win32 dependencies, so both can include it. Include it after
// HidCommon.h, whose descriptor item macros (USAGE_PAGE, FEATURE, ...) the descriptor block below uses.
//
// Windows 11 drives haptic touchpads through a Simple Haptic Controller (HID usage page 0x0E) inside
// the Precision Touchpad top-level collection:
// https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/input-haptics-implementation-guide
//
//  - Button Press Threshold (feature REPORTID_HAPTIC_BUTTON_THRESHOLD): Settings' click force, Low/Medium/High (1..3).
//  - Haptic Intensity (feature REPORTID_HAPTIC_INTENSITY): Settings' haptic feedback intensity, 0..100.
//  - Waveform/Duration List (feature REPORTID_HAPTIC_WAVEFORMS): the waveforms the host may trigger.
//  - Manual Trigger (output REPORTID_HAPTIC_MANUAL_TRIGGER): host-initiated feedback.
//
// The pad clicks by itself (device-initiated feedback): Apple feature reports 0x22 (press) and 0x23 (release)
// set that click's waveform, and output report 0x53 fires a waveform once. All three share one
// 14-byte layout with three tunable bytes, called S0, S1 and S2 here:
//
//     ?? 01 S0 78 02 S1 24 30 06 01 S2 18 48 ??
//
// The drivers' FeedbackClick/FeedbackRelease settings pack them as S0 | S1 << 8 | S2 << 16.
//
// =====================================================================================================
// CALIBRATION
// =====================================================================================================
// Everything that decides how the haptics *feel* is in the "Calibration table" section below, and only
// there. The values were picked on 2026-11-18 by feel, on one USB-C Magic Trackpad (PID 0x0324). They are
// a starting point; other people, or other pads, may prefer different ones. What was observed:
//
//   - S0 sets the click force threshold AND part of the felt strength: a higher S0 needs a harder
//     press and gives a stronger click. S0 alone (S1 = S2 = 0) is still clearly felt.
//   - S1 and S2 make the click stronger and louder without changing the force needed to click.
//     S1 = S2 = 0 is a quiet click that is still felt and still registers.
//   - One-shot pulses (0x53) get steadily stronger from 0C 00 00 to 1E 08 08; 22 0A 0D is the strongest
//     value tried that still felt pleasant.
//   - Hard limits (community notes, LinuxMagicForce): keep S1 <= 0x20 and S2 <= 0x40. Higher values are
//     considered hardware stress. The drivers clamp to these limits anyway.
//   - Windows uses Collide for window snapping/alignment and Step for the "Haptic signals" slider preview
//     (seen in a driver trace, Windows 11 build 26300). The other waveforms are rarely used by the OS.
//   - The pad varies mostly in strength, not in character. Chaining pulses (MT2_HAPTIC_PULSE_GAP_MS apart)
//     is what makes a waveform feel different: two rising pulses read as "growing", and a double tap is
//     distinct from any single pulse.
//   - Tuning session 2026-11-18: Hover, Collide, Align and Step kept their values (a fainter Hover,
//     03 00 00, was tried and rejected); Grow became two rising pulses, 10 00 00 then 1E 08 08.
//
// To recalibrate, use tools\HapticsLab (see its README.md):
//   1. It reads the table below, and has one tab per part of the haptics (click force, click feedback,
//      haptic signals, signal strength). Each tab says where Windows uses the values and what to feel for,
//      lets you try candidates on the pad without rebuilding, and keeps your picks and notes between
//      sessions. Its "Save to header" tab rewrites only the calibration table section of this file.
//   2. Rebuild and reinstall both drivers, then confirm on HapticsLab's "Driver check" tab, which goes
//      through the same HID reports Windows uses (tools\haptic_shc_test.py does that from the command line).
//   Nothing else needs to change: the HID descriptor ranges are independent of these values. Keep the layout
//   of the calibration table section as it is; HapticsLab parses and regenerates it.
//   If a waveform is added or removed, update MT2_HAPTIC_WAVEFORM_COUNT, Mt2HapticWaveforms[], the ordinal
//   usage range in MT2_HAPTICS_PTP_TLC_ITEMS (see the C_ASSERT below) and HapticsLab's waveform list together.
// =====================================================================================================
#pragma once

// Report IDs (PTP top-level collection of the Magic Trackpad 2 only). Must not collide with HidCommon.h.
#define REPORTID_HAPTIC_BUTTON_THRESHOLD 0x0A
#define REPORTID_HAPTIC_INTENSITY        0x0B
#define REPORTID_HAPTIC_WAVEFORMS        0x0C
#define REPORTID_HAPTIC_MANUAL_TRIGGER   0x0D

// Value ranges declared in the descriptor
#define MT2_HAPTIC_THRESHOLD_MIN       1   // Low
#define MT2_HAPTIC_THRESHOLD_MAX       3   // High
#define MT2_HAPTIC_INTENSITY_MAX       100

// Values used until Windows sends its own (on Windows 10, or before the first SET_FEATURE)
#define MT2_HAPTIC_THRESHOLD_DEFAULT   2   // Medium
#define MT2_HAPTIC_INTENSITY_DEFAULT   60  // = the macOS "medium" click (S1 = S2 = 0x06)

// Hardware limits for S1 and S2 (see CALIBRATION above)
#define MT2_HAPTIC_S1_LIMIT 0x20
#define MT2_HAPTIC_S2_LIMIT 0x40

// HID Haptics page (0x0E) waveform usages, from the HID Usage Tables
#define HAPTIC_WAVEFORM_NONE    0x1001
#define HAPTIC_WAVEFORM_STOP    0x1002
#define HAPTIC_WAVEFORM_PRESS   0x1006
#define HAPTIC_WAVEFORM_RELEASE 0x1007
#define HAPTIC_WAVEFORM_HOVER   0x1008
#define HAPTIC_WAVEFORM_COLLIDE 0x1012
#define HAPTIC_WAVEFORM_ALIGN   0x1013
#define HAPTIC_WAVEFORM_STEP    0x1014
#define HAPTIC_WAVEFORM_GROW    0x1015

// Ordinals 1 and 2 are the implicit None and Stop; the waveform list starts at ordinal 3.
#define MT2_HAPTIC_ORDINAL_NONE  1
#define MT2_HAPTIC_ORDINAL_STOP  2
#define MT2_HAPTIC_ORDINAL_FIRST 3
#define MT2_HAPTIC_WAVEFORM_COUNT 7

typedef struct _MT2_HAPTIC_PARAMS {
	UCHAR S0;
	UCHAR S1;
	UCHAR S2;
} MT2_HAPTIC_PARAMS;

#define MT2_HAPTIC_MAX_PULSES 3

typedef struct _MT2_HAPTIC_WAVEFORM {
	USHORT            WaveformId;  // HID Haptics page usage
	UCHAR             DurationMs;  // reported in the Duration List; must be 1..50
	UCHAR             PulseCount;  // 1..MT2_HAPTIC_MAX_PULSES
	MT2_HAPTIC_PARAMS Pulses[MT2_HAPTIC_MAX_PULSES]; // 0x53 actuations at full (100) intensity
} MT2_HAPTIC_WAVEFORM;

// =====================================================================================================
// Calibration table. See CALIBRATION at the top of this file before changing anything here.
// =====================================================================================================

// Button Press Threshold (Low, Medium, High) -> S0 of the press (0x22) and release (0x23) waveforms.
static const UCHAR Mt2HapticPressS0[3] = { 0x10, 0x17, 0x1E };
static const UCHAR Mt2HapticReleaseS0[3] = { 0x10, 0x14, 0x18 };

// Haptic Intensity 0..100 -> S1 = S2 of the press waveform, linearly from 0 to this value.
#define MT2_HAPTIC_PRESS_S12_AT_MAX 0x0A

// Release waveform S1 = S2: this value at or above the intensity below, 0 otherwise.
#define MT2_HAPTIC_RELEASE_S12_STRONG      0x02
#define MT2_HAPTIC_RELEASE_STRONG_INTENSITY 80

// Host-initiated waveforms, in ordinal order starting at MT2_HAPTIC_ORDINAL_FIRST.
// The touchpad rules: Hover, Collide, Align, Step and Grow are mandatory; Press and Release are
// optional but come as a pair; Click (0x1003) and continuous waveforms are not allowed.
// A waveform with several pulses fires them MT2_HAPTIC_PULSE_GAP_MS apart (start to start). Its
// DurationMs should cover the whole chain: about (pulses - 1) * gap + 15.
static const MT2_HAPTIC_WAVEFORM Mt2HapticWaveforms[MT2_HAPTIC_WAVEFORM_COUNT] = {
	{ HAPTIC_WAVEFORM_PRESS,   15, 1, { { 0x17, 0x06, 0x06 } } },                   // = the medium click
	{ HAPTIC_WAVEFORM_RELEASE, 15, 1, { { 0x14, 0x00, 0x00 } } },                   // = the medium release
	{ HAPTIC_WAVEFORM_HOVER,   15, 1, { { 0x0C, 0x00, 0x00 } } },                   // weakest that is clearly felt
	{ HAPTIC_WAVEFORM_COLLIDE, 15, 1, { { 0x14, 0x00, 0x00 } } },                   // soft
	{ HAPTIC_WAVEFORM_ALIGN,   15, 1, { { 0x15, 0x04, 0x04 } } },                   // crisp, light
	{ HAPTIC_WAVEFORM_STEP,    15, 1, { { 0x1E, 0x08, 0x08 } } },                   // firm
	{ HAPTIC_WAVEFORM_GROW,    45, 2, { { 0x10, 0x00, 0x00 }, { 0x1E, 0x08, 0x08 } } }, // rising: weak, then firm
};

// Time between the starts of chained pulses. Over Bluetooth a send takes about 70 ms, so pulses
// land further apart there.
#define MT2_HAPTIC_PULSE_GAP_MS 30

// "Haptic signals" intensity (Manual Trigger Intensity, 0..100) -> how strongly a waveform above fires.
// S1/S2 scale linearly with the intensity. S0 scales from MT2_HAPTIC_PULSE_S0_MIN_PERCENT of its table
// value (at intensity 1) to 100 % (at 100), and never below MT2_HAPTIC_PULSE_S0_FLOOR, so even the lowest
// setting is felt. Intensity 0 fires nothing. Windows sends 25, 50, 75 or 100.
#define MT2_HAPTIC_PULSE_S0_MIN_PERCENT 50
#define MT2_HAPTIC_PULSE_S0_FLOOR       0x0C

// =====================================================================================================
// End of calibration table
// =====================================================================================================

#include <pshpack1.h>

typedef struct _MT2_HAPTIC_BUTTON_THRESHOLD_REPORT {
	UCHAR ReportID;
	UCHAR Threshold;           // MT2_HAPTIC_THRESHOLD_MIN..MAX
} MT2_HAPTIC_BUTTON_THRESHOLD_REPORT, *PMT2_HAPTIC_BUTTON_THRESHOLD_REPORT;

typedef struct _MT2_HAPTIC_INTENSITY_REPORT {
	UCHAR ReportID;
	UCHAR Intensity;           // 0..MT2_HAPTIC_INTENSITY_MAX
} MT2_HAPTIC_INTENSITY_REPORT, *PMT2_HAPTIC_INTENSITY_REPORT;

typedef struct _MT2_HAPTIC_WAVEFORM_LIST_REPORT {
	UCHAR  ReportID;
	USHORT WaveformList[MT2_HAPTIC_WAVEFORM_COUNT];
	UCHAR  DurationList[MT2_HAPTIC_WAVEFORM_COUNT];
} MT2_HAPTIC_WAVEFORM_LIST_REPORT, *PMT2_HAPTIC_WAVEFORM_LIST_REPORT;

typedef struct _MT2_HAPTIC_MANUAL_TRIGGER_REPORT {
	UCHAR ReportID;
	UCHAR ManualTrigger;       // ordinal: 1 None, 2 Stop, 3.. Mt2HapticWaveforms[]
	UCHAR Intensity;           // 0..MT2_HAPTIC_INTENSITY_MAX
} MT2_HAPTIC_MANUAL_TRIGGER_REPORT, *PMT2_HAPTIC_MANUAL_TRIGGER_REPORT;

#include <poppack.h>

C_ASSERT(sizeof(MT2_HAPTIC_BUTTON_THRESHOLD_REPORT) == 2);
C_ASSERT(sizeof(MT2_HAPTIC_INTENSITY_REPORT) == 2);
C_ASSERT(sizeof(MT2_HAPTIC_WAVEFORM_LIST_REPORT) == 1 + MT2_HAPTIC_WAVEFORM_COUNT * 3);
C_ASSERT(sizeof(MT2_HAPTIC_MANUAL_TRIGGER_REPORT) == 3);
// The descriptor below declares ordinals 3..9 and REPORT_COUNT 7 for both lists.
C_ASSERT(MT2_HAPTIC_WAVEFORM_COUNT == 7);
C_ASSERT(MT2_HAPTIC_PULSE_GAP_MS >= 20); // the drivers' minimum gap between actuations
C_ASSERT(sizeof(Mt2HapticPressS0) == MT2_HAPTIC_THRESHOLD_MAX - MT2_HAPTIC_THRESHOLD_MIN + 1);
C_ASSERT(sizeof(Mt2HapticReleaseS0) == sizeof(Mt2HapticPressS0));

// Descriptor items appended to the Magic Trackpad 2 PTP top-level collection, before its END_COLLECTION.
// Layout follows the "Sample Haptic Touchpad Descriptor" of the haptics implementation guide: the Button
// Press Threshold directly in the PTP collection, then one Simple Haptic Controller logical collection per
// report. Repeat Count, Retrigger Period and Waveform Cutoff Time are optional (all or none) and omitted.
#define MT2_HAPTICS_PTP_TLC_ITEMS \
	USAGE_PAGE, 0x0d, /* Usage Page: Digitizer */ \
	REPORT_ID, REPORTID_HAPTIC_BUTTON_THRESHOLD, \
	USAGE, 0xb0, /* Usage: Button Press Threshold */ \
	LOGICAL_MINIMUM, MT2_HAPTIC_THRESHOLD_MIN, \
	LOGICAL_MAXIMUM, MT2_HAPTIC_THRESHOLD_MAX, \
	PHYSICAL_MINIMUM, 0x6e, /* Physical Minimum: 110 g */ \
	PHYSICAL_MAXIMUM_2, 0xbe, 0x00, /* Physical Maximum: 190 g */ \
	UNIT_EXPONENT, 0x00, \
	UNIT_2, 0x01, 0x01, /* Unit: SI Linear, Mass (gram) */ \
	REPORT_SIZE, 0x08, \
	REPORT_COUNT, 0x01, \
	FEATURE, 0x02, /* Feature: (Data, Var, Abs) */ \
	PHYSICAL_MINIMUM, 0x00, \
	PHYSICAL_MAXIMUM, 0x00, \
	UNIT, 0x00, \
	USAGE_PAGE, 0x0e, /* Usage Page: Haptics */ \
	USAGE, 0x01, /* Usage: Simple Haptic Controller */ \
	BEGIN_COLLECTION, 0x02, /* Begin Collection: Logical */ \
		REPORT_ID, REPORTID_HAPTIC_INTENSITY, \
		USAGE, 0x23, /* Usage: Intensity */ \
		LOGICAL_MINIMUM, 0x00, \
		LOGICAL_MAXIMUM, MT2_HAPTIC_INTENSITY_MAX, \
		REPORT_SIZE, 0x08, \
		REPORT_COUNT, 0x01, \
		FEATURE, 0x02, /* Feature: (Data, Var, Abs) */ \
	END_COLLECTION, \
	USAGE, 0x01, /* Usage: Simple Haptic Controller */ \
	BEGIN_COLLECTION, 0x02, /* Begin Collection: Logical */ \
		REPORT_ID, REPORTID_HAPTIC_WAVEFORMS, \
		USAGE, 0x10, /* Usage: Waveform List */ \
		BEGIN_COLLECTION, 0x02, /* Begin Collection: Logical */ \
			USAGE_PAGE, 0x0a, /* Usage Page: Ordinal */ \
			USAGE_MINIMUM, 0x03, /* Usage Minimum: Instance 3 */ \
			USAGE_MAXIMUM, 0x09, /* Usage Maximum: Instance 9 */ \
			LOGICAL_MINIMUM_2, 0x01, 0x10, /* Logical Minimum: 0x1001 */ \
			LOGICAL_MAXIMUM_2, 0xff, 0x2f, /* Logical Maximum: 0x2FFF */ \
			REPORT_SIZE, 0x10, \
			REPORT_COUNT, MT2_HAPTIC_WAVEFORM_COUNT, \
			FEATURE, 0x02, /* Feature: (Data, Var, Abs) */ \
		END_COLLECTION, \
		USAGE_PAGE, 0x0e, /* Usage Page: Haptics */ \
		USAGE, 0x11, /* Usage: Duration List */ \
		BEGIN_COLLECTION, 0x02, /* Begin Collection: Logical */ \
			USAGE_PAGE, 0x0a, /* Usage Page: Ordinal */ \
			USAGE_MINIMUM, 0x03, /* Usage Minimum: Instance 3 */ \
			USAGE_MAXIMUM, 0x09, /* Usage Maximum: Instance 9 */ \
			LOGICAL_MINIMUM, 0x00, \
			LOGICAL_MAXIMUM, 0x32, /* Logical Maximum: 50 */ \
			PHYSICAL_MINIMUM, 0x00, \
			PHYSICAL_MAXIMUM, 0x32, /* Physical Maximum: 50 ms */ \
			UNIT_EXPONENT, 0x0d, /* Unit exponent: -3 */ \
			UNIT_2, 0x01, 0x10, /* Unit: SI Linear, Time (s) */ \
			REPORT_SIZE, 0x08, \
			REPORT_COUNT, MT2_HAPTIC_WAVEFORM_COUNT, \
			FEATURE, 0x02, /* Feature: (Data, Var, Abs) */ \
		END_COLLECTION, \
		PHYSICAL_MAXIMUM, 0x00, \
		UNIT_EXPONENT, 0x00, \
		UNIT, 0x00, \
	END_COLLECTION, \
	USAGE_PAGE, 0x0e, /* Usage Page: Haptics */ \
	USAGE, 0x01, /* Usage: Simple Haptic Controller */ \
	BEGIN_COLLECTION, 0x02, /* Begin Collection: Logical */ \
		REPORT_ID, REPORTID_HAPTIC_MANUAL_TRIGGER, \
		USAGE, 0x21, /* Usage: Manual Trigger */ \
		LOGICAL_MINIMUM, MT2_HAPTIC_ORDINAL_NONE, \
		LOGICAL_MAXIMUM, MT2_HAPTIC_ORDINAL_FIRST + MT2_HAPTIC_WAVEFORM_COUNT - 1, \
		REPORT_SIZE, 0x08, \
		REPORT_COUNT, 0x01, \
		OUTPUT, 0x02, /* Output: (Data, Var, Abs) */ \
		USAGE, 0x23, /* Usage: Intensity */ \
		LOGICAL_MINIMUM, 0x00, \
		LOGICAL_MAXIMUM, MT2_HAPTIC_INTENSITY_MAX, \
		OUTPUT, 0x02, /* Output: (Data, Var, Abs) */ \
	END_COLLECTION

// Packs S0/S1/S2 into the FeedbackClick/FeedbackRelease encoding, clamping S1 and S2 to the hardware limits.
static __inline ULONG
Mt2HapticPack(
	_In_ UCHAR S0,
	_In_ ULONG S1,
	_In_ ULONG S2
)
{
	if (S1 > MT2_HAPTIC_S1_LIMIT) S1 = MT2_HAPTIC_S1_LIMIT;
	if (S2 > MT2_HAPTIC_S2_LIMIT) S2 = MT2_HAPTIC_S2_LIMIT;
	return (ULONG)S0 | (S1 << 8) | (S2 << 16);
}

// Windows' Button Press Threshold and Haptic Intensity -> FeedbackClick/FeedbackRelease encoding.
// Out-of-range input is clamped.
static __inline VOID
Mt2HapticClickFromWindows(
	_In_ UCHAR Threshold,
	_In_ UCHAR Intensity,
	_Out_ ULONG* FeedbackClick,
	_Out_ ULONG* FeedbackRelease
)
{
	ULONG level, pressS12, releaseS12;

	if (Threshold < MT2_HAPTIC_THRESHOLD_MIN) Threshold = MT2_HAPTIC_THRESHOLD_MIN;
	if (Threshold > MT2_HAPTIC_THRESHOLD_MAX) Threshold = MT2_HAPTIC_THRESHOLD_MAX;
	if (Intensity > MT2_HAPTIC_INTENSITY_MAX) Intensity = MT2_HAPTIC_INTENSITY_MAX;
	level = (ULONG)(Threshold - MT2_HAPTIC_THRESHOLD_MIN);

	// Rounded linear scale
	pressS12 = ((ULONG)Intensity * MT2_HAPTIC_PRESS_S12_AT_MAX + MT2_HAPTIC_INTENSITY_MAX / 2) / MT2_HAPTIC_INTENSITY_MAX;
	releaseS12 = (Intensity >= MT2_HAPTIC_RELEASE_STRONG_INTENSITY) ? MT2_HAPTIC_RELEASE_S12_STRONG : 0;

	*FeedbackClick = Mt2HapticPack(Mt2HapticPressS0[level], pressS12, pressS12);
	*FeedbackRelease = Mt2HapticPack(Mt2HapticReleaseS0[level], releaseS12, releaseS12);
}

// Decides which click waveform the pad gets.
//  - Windows' values, once Windows has sent them, unless the user chose to override Windows.
//  - Otherwise the FeedbackClick/FeedbackRelease registry settings (Control Panel presets). This is also
//    what Windows 10 gets, which never sends the haptic feature reports.
static __inline VOID
Mt2HapticResolveClick(
	_In_ BOOLEAN OverrideWindows,
	_In_ BOOLEAN WindowsValuesValid,
	_In_ UCHAR WindowsThreshold,
	_In_ UCHAR WindowsIntensity,
	_In_ ULONG RegistryClick,
	_In_ ULONG RegistryRelease,
	_Out_ ULONG* FeedbackClick,
	_Out_ ULONG* FeedbackRelease
)
{
	if (!OverrideWindows && WindowsValuesValid) {
		Mt2HapticClickFromWindows(WindowsThreshold, WindowsIntensity, FeedbackClick, FeedbackRelease);
	}
	else {
		*FeedbackClick = RegistryClick;
		*FeedbackRelease = RegistryRelease;
	}
}

// Fills the Waveform List / Duration List feature report from Mt2HapticWaveforms[].
static __inline VOID
Mt2HapticFillWaveformListReport(
	_Out_ PMT2_HAPTIC_WAVEFORM_LIST_REPORT Report
)
{
	ULONG i;

	Report->ReportID = REPORTID_HAPTIC_WAVEFORMS;
	for (i = 0; i < MT2_HAPTIC_WAVEFORM_COUNT; i++) {
		Report->WaveformList[i] = Mt2HapticWaveforms[i].WaveformId;
		Report->DurationList[i] = Mt2HapticWaveforms[i].DurationMs;
	}
}

// A Manual Trigger output report -> the 0x53 actuations to fire, in the FeedbackClick encoding, fired
// MT2_HAPTIC_PULSE_GAP_MS apart. Returns how many (0..MT2_HAPTIC_MAX_PULSES); 0 when nothing should fire:
// None, Stop (the pad has nothing to stop), intensity 0, or an ordinal outside the waveform list.
static __inline ULONG
Mt2HapticPulsesForTrigger(
	_In_ UCHAR Ordinal,
	_In_ UCHAR Intensity,
	_Out_writes_(MT2_HAPTIC_MAX_PULSES) ULONG* Feedback
)
{
	const MT2_HAPTIC_WAVEFORM* waveform;
	ULONG s0Percent, s0, s1, s2, i;

	for (i = 0; i < MT2_HAPTIC_MAX_PULSES; i++) {
		Feedback[i] = 0;
	}
	if (Ordinal < MT2_HAPTIC_ORDINAL_FIRST || Ordinal >= MT2_HAPTIC_ORDINAL_FIRST + MT2_HAPTIC_WAVEFORM_COUNT) {
		return 0;
	}
	if (Intensity == 0) {
		return 0;
	}
	if (Intensity > MT2_HAPTIC_INTENSITY_MAX) Intensity = MT2_HAPTIC_INTENSITY_MAX;

	waveform = &Mt2HapticWaveforms[Ordinal - MT2_HAPTIC_ORDINAL_FIRST];
	s0Percent = MT2_HAPTIC_PULSE_S0_MIN_PERCENT
		+ ((100 - MT2_HAPTIC_PULSE_S0_MIN_PERCENT) * (ULONG)Intensity) / MT2_HAPTIC_INTENSITY_MAX;

	for (i = 0; i < waveform->PulseCount && i < MT2_HAPTIC_MAX_PULSES; i++) {
		const MT2_HAPTIC_PARAMS* base = &waveform->Pulses[i];
		s0 = ((ULONG)base->S0 * s0Percent + 50) / 100;
		if (s0 < MT2_HAPTIC_PULSE_S0_FLOOR) s0 = MT2_HAPTIC_PULSE_S0_FLOOR;
		s1 = ((ULONG)base->S1 * Intensity + MT2_HAPTIC_INTENSITY_MAX / 2) / MT2_HAPTIC_INTENSITY_MAX;
		s2 = ((ULONG)base->S2 * Intensity + MT2_HAPTIC_INTENSITY_MAX / 2) / MT2_HAPTIC_INTENSITY_MAX;
		Feedback[i] = Mt2HapticPack((UCHAR)s0, s1, s2);
	}
	return i;
}

// Packs the Windows click state into one LONG so it can be published and read atomically
// (written from SET_FEATURE, read by whichever path pushes the click waveform to the pad).
#define MT2_HAPTIC_STATE_VALID 0x10000
#define MT2_HAPTIC_STATE_MAKE(Threshold, Intensity) \
	((LONG)(MT2_HAPTIC_STATE_VALID | ((ULONG)(UCHAR)(Intensity) << 8) | (UCHAR)(Threshold)))
#define MT2_HAPTIC_STATE_IS_VALID(State)   (((ULONG)(State) & MT2_HAPTIC_STATE_VALID) != 0)
#define MT2_HAPTIC_STATE_THRESHOLD(State)  ((UCHAR)((ULONG)(State) & 0xFF))
#define MT2_HAPTIC_STATE_INTENSITY(State)  ((UCHAR)(((ULONG)(State) >> 8) & 0xFF))

#define MT2_HAPTIC_UNCHANGED (-1)

// Stores one Windows value (the other passes MT2_HAPTIC_UNCHANGED) and marks the state valid.
// A value Windows has not sent yet keeps its default. Safe against a concurrent update of the other value.
static __inline VOID
Mt2HapticUpdateState(
	_Inout_ volatile LONG* State,
	_In_ LONG Threshold,
	_In_ LONG Intensity
)
{
	LONG oldState, newState;
	UCHAR threshold, intensity;

	do {
		oldState = *State;
		threshold = MT2_HAPTIC_STATE_IS_VALID(oldState) ? MT2_HAPTIC_STATE_THRESHOLD(oldState) : MT2_HAPTIC_THRESHOLD_DEFAULT;
		intensity = MT2_HAPTIC_STATE_IS_VALID(oldState) ? MT2_HAPTIC_STATE_INTENSITY(oldState) : MT2_HAPTIC_INTENSITY_DEFAULT;
		if (Threshold != MT2_HAPTIC_UNCHANGED) threshold = (UCHAR)Threshold;
		if (Intensity != MT2_HAPTIC_UNCHANGED) intensity = (UCHAR)Intensity;
		newState = MT2_HAPTIC_STATE_MAKE(threshold, intensity);
	} while (InterlockedCompareExchange(State, newState, oldState) != oldState);
}

// Current Windows values, or the defaults if Windows has not sent them (for GET_FEATURE).
static __inline UCHAR
Mt2HapticStateThreshold(
	_In_ LONG State
)
{
	return MT2_HAPTIC_STATE_IS_VALID(State) ? MT2_HAPTIC_STATE_THRESHOLD(State) : MT2_HAPTIC_THRESHOLD_DEFAULT;
}

static __inline UCHAR
Mt2HapticStateIntensity(
	_In_ LONG State
)
{
	return MT2_HAPTIC_STATE_IS_VALID(State) ? MT2_HAPTIC_STATE_INTENSITY(State) : MT2_HAPTIC_INTENSITY_DEFAULT;
}
