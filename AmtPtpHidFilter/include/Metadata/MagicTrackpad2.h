// MagicTrackpad2.h: Magic Trackpad 2 specific HID information
#pragma once

/* Per-contact extras appended to every finger collection, right after Y (12 bytes).
   Width (0x48) / Height (0x49): X/Y extent of the touch ellipse's bounding box, computed
   from TouchMajor/TouchMinor << 2 (the scaling Linux hid-magicmouse uses) rotated by the
   3-bit Orientation. Apple's axes are direction-independent, so both share a single
   physical scale, taken from the X axis (cm, exp -2, 1020 = 2.14 cm).
   Pressure (0x30): the Apple pressure byte (0..255 in practice). Its logical range is
   0..1275 because the spec requires it to equal Mechanical Force's, which is the sum
   of up to 5 contacts. Then a vendor page (0xFF00) for what has no standard usage:
   0x02 Size, 0x03/0x04 raw TouchMajor/TouchMinor (the unrotated ellipse axes),
   0x05 Orientation (raw 3-bit; 4 = major axis along Y, 22.5 deg per step),
   0x06 Finger type, 0x07 State. Restores Report Size/Count afterwards. */
#define AAPL_MAGIC_TRACKPAD2_PTP_FINGER_EXTRAS \
		USAGE_PAGE, 0x0d, /* Usage Page: Digitizer */ \
		LOGICAL_MAXIMUM_2, 0xfc, 0x03, /* Logical Maximum: 1020 (255 << 2) */ \
		PHYSICAL_MAXIMUM_2, 0xd6, 0x00, /* Physical Maximum: 214 (1020 x 1600 / 7612, in 0.01 cm) */ \
		REPORT_SIZE, 0x10, /* Report Size: 16 */ \
		REPORT_COUNT, 0x02, /* Report Count: 2 */ \
		USAGE, 0x48, /* Usage: Width */ \
		USAGE, 0x49, /* Usage: Height */ \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		PHYSICAL_MAXIMUM, 0x00, /* Physical Maximum: 0 */ \
		UNIT_EXPONENT, 0x00, /* Unit exponent: 0 */ \
		UNIT, 0x00, /* Unit: None */ \
		LOGICAL_MAXIMUM_2, 0xfb, 0x04, /* Logical Maximum: 1275 (same as Mechanical Force) */ \
		REPORT_COUNT, 0x01, /* Report Count: 1 */ \
		USAGE, 0x30, /* Usage: Tip Pressure */ \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		USAGE_PAGE_1, 0x00, 0xff, /* Usage Page: Vendor defined (0xFF00) */ \
		LOGICAL_MAXIMUM_2, 0xff, 0x00, /* Logical Maximum: 255 */ \
		REPORT_SIZE, 0x08, /* Report Size: 8 */ \
		REPORT_COUNT, 0x06, /* Report Count: 6 */ \
		USAGE, 0x02, /* Size */ \
		USAGE, 0x03, /* TouchMajor (raw ellipse major axis) */ \
		USAGE, 0x04, /* TouchMinor (raw ellipse minor axis) */ \
		USAGE, 0x05, /* Orientation */ \
		USAGE, 0x06, /* Finger type */ \
		USAGE, 0x07, /* State */ \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		REPORT_SIZE, 0x10, /* Report Size: 16 (as before this block) */ \
		REPORT_COUNT, 0x01 /* Report Count: 1 (as before this block) */ \

#define AAPL_MAGIC_TRACKPAD2_PTP_FINGER_COLLECTION_1 \
	BEGIN_COLLECTION, 0x02, /* Begin Collection: Logical */ \
		/* Begin a byte */ \
		LOGICAL_MAXIMUM, 0x01, /* Logical Maximum: 1 */ \
		USAGE, 0x47, /* Usage: Confidence */ \
		USAGE, 0x42, /* Usage: Tip switch */ \
		REPORT_COUNT, 0x02, /* Report Count: 2 */ \
		REPORT_SIZE, 0x01, /* Report Size: 1 */ \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		REPORT_SIZE, 0x01, /* Report Size: 1 */ \
		REPORT_COUNT, 0x06, /* Report Count: 6 */ \
		INPUT, 0x03, /* Input: (Const, Var, Abs) */ \
		/* End of a byte */ \
		/* Begin of 4 bytes */ \
		REPORT_COUNT, 0x01, /* Report Count: 1 */ \
		REPORT_SIZE, 0x20, /* Report Size: 0x20 (4 bytes) */ \
		LOGICAL_MAXIMUM_3, 0xff, 0xff, 0xff, 0xff, /* Logical Maximum: 0xffffffff */ \
		USAGE, 0x51, /* Usage: Contract Identifier */ \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		/* End of 4 bytes */ \
		/* Begin of 4 bytes */ \
		/* Size is hard-coded at this moment */ \
		USAGE_PAGE, 0x01, /* Usage Page: Generic Desktop */ \
		LOGICAL_MAXIMUM_2, 0xbc, 0x1d, /* Logical Maximum: 7612 (See defintion) */ \
		REPORT_SIZE, 0x10, /* Report Size: 0x10 (2 bytes) */ \
		UNIT_EXPONENT, 0x0e, /* Unit exponent: -2 */ \
		UNIT, 0x11, /* Unit: SI Length (cm) */ \
		USAGE, 0x30, /* Usage: X */ \
		PHYSICAL_MAXIMUM_2, 0x40, 0x06, /* Physical Maximum: 1600 (See Apple Spec) */ \
		REPORT_COUNT, 0x01, /* Report count: 1 */ \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		PHYSICAL_MAXIMUM_2, 0x7d, 0x04, /* Physical Maximum: 1149 (See Apple Spec) */ \
		LOGICAL_MAXIMUM_2, 0xc9, 0x13, /* Logical Maximum: 5065 (See definition) */ \
		USAGE, 0x31, /* Usage: Y */ \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		/* End of 4 bytes */ \
		AAPL_MAGIC_TRACKPAD2_PTP_FINGER_EXTRAS, \
	END_COLLECTION /* End Collection */ \

#define AAPL_MAGIC_TRACKPAD2_PTP_FINGER_COLLECTION_2 \
	BEGIN_COLLECTION, 0x02, /* Begin Collection: Logical */ \
		/* Begin a byte */ \
		LOGICAL_MAXIMUM, 0x01, /* Logical Maximum: 1 */ \
		USAGE, 0x47, /* Usage: Confidence */ \
		USAGE, 0x42, /* Usage: Tip switch */ \
		REPORT_COUNT, 0x02, /* Report Count: 2 */ \
		REPORT_SIZE, 0x01, /* Report Size: 1 */ \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		REPORT_SIZE, 0x01, /* Report Size: 1 */ \
		REPORT_COUNT, 0x06, /* Report Count: 6 */ \
		INPUT, 0x03, /* Input: (Const, Var, Abs) */ \
		/* End of a byte */ \
		/* Begin of 4 bytes */ \
		REPORT_COUNT, 0x01, /* Report Count: 1 */ \
		REPORT_SIZE, 0x20, /* Report Size: 0x20 (4 bytes) */ \
		LOGICAL_MAXIMUM_3, 0xff, 0xff, 0xff, 0xff, /* Logical Maximum: 0xffffffff */ \
		USAGE, 0x51, /* Usage: Contract Identifier */ \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		/* End of 4 bytes */ \
		/* Begin of 4 bytes */ \
		/* Size is hard-coded at this moment */ \
		USAGE_PAGE, 0x01, /* Usage Page: Generic Desktop */ \
		LOGICAL_MAXIMUM_2, 0xbc, 0x1d, /* Logical Maximum: 7612 (See defintion) */ \
		REPORT_SIZE, 0x10, /* Report Size: 0x10 (2 bytes) */ \
		UNIT_EXPONENT, 0x0e, /* Unit exponent: -2 */ \
		UNIT, 0x11, /* Unit: SI Length (cm) */ \
		USAGE, 0x30, /* Usage: X */ \
		PHYSICAL_MAXIMUM_2, 0x40, 0x06, /* Physical Maximum: 1600 (See Apple Spec) */ \
		REPORT_COUNT, 0x01, /* Report count: 1 */ \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		PHYSICAL_MAXIMUM_2, 0x7d, 0x04, /* Physical Maximum: 1149 (See Apple Spec) */ \
		LOGICAL_MAXIMUM_2, 0xc9, 0x13, /* Logical Maximum: 5065 (See definition) */ \
		USAGE, 0x31, /* Usage: Y */ \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		/* End of 4 bytes */ \
		AAPL_MAGIC_TRACKPAD2_PTP_FINGER_EXTRAS, \
	END_COLLECTION /* End Collection */ \

#define AAPL_MAGIC_TRACKPAD2_PTP_TLC \
	USAGE_PAGE, 0x0d, /* Usage Page: Digitizer */ \
	USAGE, 0x05, /* Usage: Touch Pad */ \
	BEGIN_COLLECTION, 0x01, /* Begin Collection: Application */ \
		REPORT_ID, REPORTID_MULTITOUCH, /* Report ID: Multi-touch */ \
		USAGE, 0x22, /* Usage: Finger */ \
		AAPL_MAGIC_TRACKPAD2_PTP_FINGER_COLLECTION_1, /* 1 */ \
		USAGE_PAGE, 0x0d, /* Usage Page: Digitizer */ \
		USAGE, 0x22, /* Usage: Finger */ \
		AAPL_MAGIC_TRACKPAD2_PTP_FINGER_COLLECTION_1, /* 2 */ \
		USAGE_PAGE, 0x0d, /* Usage Page: Digitizer */ \
		USAGE, 0x22, /* Usage: Finger */ \
		AAPL_MAGIC_TRACKPAD2_PTP_FINGER_COLLECTION_2, /* 3 */ \
		USAGE_PAGE, 0x0d, /* Usage Page: Digitizer */ \
		USAGE, 0x22, /* Usage: Finger */ \
		AAPL_MAGIC_TRACKPAD2_PTP_FINGER_COLLECTION_1, /* 4 */ \
		USAGE_PAGE, 0x0d, /* Usage Page: Digitizer */ \
		USAGE, 0x22, /* Usage: Finger */ \
		AAPL_MAGIC_TRACKPAD2_PTP_FINGER_COLLECTION_2, /* 5 */ \
		USAGE_PAGE, 0x0d, /* Usage Page: Digitizer */ \
		UNIT_EXPONENT, 0x0c, /* Unit exponent: -4 */ \
		UNIT_2, 0x01, 0x10, /* Time: Second */ \
		PHYSICAL_MAXIMUM_3, 0xff, 0xff, 0x00, 0x00, \
		LOGICAL_MAXIMUM_3, 0xff, 0xff, 0x00, 0x00, \
		USAGE, 0x56, /* Usage: Scan Time */ \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		USAGE, 0x54, /* Usage: Contact Count */ \
		LOGICAL_MAXIMUM, 0x7f, \
		REPORT_SIZE, 0x08, \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		USAGE_PAGE_1, 0x20, 0x00, /* Usage Page: Sensors */ \
		USAGE_2, 0x94, 0x04, /* Usage: Mechanical Force (sum of contact pressures, same units) */ \
		PHYSICAL_MAXIMUM, 0x00, \
		UNIT_EXPONENT, 0x00, \
		UNIT, 0x00, \
		LOGICAL_MAXIMUM_2, 0xfb, 0x04, /* Logical Maximum: 1275 (5 x 255) */ \
		REPORT_SIZE, 0x10, \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		USAGE_PAGE, 0x09, /* Usage Page: Button */ \
		USAGE, 0x01, /* Button 1 */ \
		LOGICAL_MAXIMUM, 0x01, \
		REPORT_SIZE, 0x01, \
		INPUT, 0x02, /* Input: (Data, Var, Abs) */ \
		REPORT_COUNT, 0x07, \
		INPUT, 0x03, /* Input: (Const, Var, Abs) */ \
		USAGE_PAGE, 0x0d, /* Usage Page: Digitizer */ \
		REPORT_ID, REPORTID_DEVICE_CAPS, \
		USAGE, 0x55, /* Usage: Maximum Contacts */ \
		USAGE, 0x59, /* Usage: Touchpad Button Type*/ \
		LOGICAL_MINIMUM, 0x00, \
		LOGICAL_MAXIMUM_2, 0xff, 0x00, \
		REPORT_SIZE, 0x08, \
		REPORT_COUNT, 0x02, \
		FEATURE, 0x02, \
		USAGE_PAGE_1, 0x00, 0xff, \
		REPORT_ID, REPORTID_PTPHQA, \
		USAGE, 0xc5, \
		LOGICAL_MINIMUM, 0x00, \
		LOGICAL_MAXIMUM_2, 0xff, 0x00, \
		REPORT_SIZE, 0x08, \
		REPORT_COUNT_2, 0x00, 0x01, \
		FEATURE, 0x02, \
	END_COLLECTION /* End Collection */
