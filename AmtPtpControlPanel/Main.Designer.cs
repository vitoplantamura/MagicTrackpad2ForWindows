
namespace AmtPtpControlPanel
{
    partial class Main
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Main));
            this.ctlInstallDriver = new System.Windows.Forms.Button();
            this.ctlApply = new System.Windows.Forms.Button();
            this.ctlFeedback = new System.Windows.Forms.TrackBar();
            this.ctlLightLabel = new System.Windows.Forms.Label();
            this.ctlMediumLabel = new System.Windows.Forms.Label();
            this.ctlFirmLabel = new System.Windows.Forms.Label();
            this.ctlSilentClicking = new System.Windows.Forms.CheckBox();
            this.ctlMacOSClickOptions = new System.Windows.Forms.RadioButton();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.ctlMaximumFeedback = new System.Windows.Forms.RadioButton();
            this.ctlDisableFeedback = new System.Windows.Forms.RadioButton();
            this.ctlFocusHack = new System.Windows.Forms.TextBox();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.ctlStopSizeLabel = new System.Windows.Forms.Label();
            this.ctlStopSizeValue = new System.Windows.Forms.TextBox();
            this.ctlStopPressureLabel = new System.Windows.Forms.Label();
            this.ctlStopPressureValue = new System.Windows.Forms.TextBox();
            this.ctlStopSize = new System.Windows.Forms.RadioButton();
            this.ctlStopPressure = new System.Windows.Forms.RadioButton();
            this.ctlStopDoNothing = new System.Windows.Forms.RadioButton();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.ctlIgnoreNearFingers = new System.Windows.Forms.CheckBox();
            this.ctlIgnoreButtonFinger = new System.Windows.Forms.CheckBox();
            this.ctlPalmRejection = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.ctlFeedback)).BeginInit();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.groupBox4.SuspendLayout();
            this.SuspendLayout();
            // 
            // ctlInstallDriver
            // 
            this.ctlInstallDriver.Location = new System.Drawing.Point(13, 537);
            this.ctlInstallDriver.Name = "ctlInstallDriver";
            this.ctlInstallDriver.Size = new System.Drawing.Size(195, 33);
            this.ctlInstallDriver.TabIndex = 5;
            this.ctlInstallDriver.Text = "Install Driver";
            this.ctlInstallDriver.UseVisualStyleBackColor = true;
            this.ctlInstallDriver.Click += new System.EventHandler(this.ctlInstallDriver_Click);
            // 
            // ctlApply
            // 
            this.ctlApply.Location = new System.Drawing.Point(608, 537);
            this.ctlApply.Name = "ctlApply";
            this.ctlApply.Size = new System.Drawing.Size(195, 33);
            this.ctlApply.TabIndex = 0;
            this.ctlApply.Text = "Apply";
            this.ctlApply.UseVisualStyleBackColor = true;
            this.ctlApply.Click += new System.EventHandler(this.ctlApply_Click);
            // 
            // ctlFeedback
            // 
            this.ctlFeedback.Location = new System.Drawing.Point(53, 106);
            this.ctlFeedback.Maximum = 2;
            this.ctlFeedback.Name = "ctlFeedback";
            this.ctlFeedback.Size = new System.Drawing.Size(227, 56);
            this.ctlFeedback.TabIndex = 2;
            this.ctlFeedback.Value = 1;
            // 
            // ctlLightLabel
            // 
            this.ctlLightLabel.AutoSize = true;
            this.ctlLightLabel.Location = new System.Drawing.Point(50, 145);
            this.ctlLightLabel.Name = "ctlLightLabel";
            this.ctlLightLabel.Size = new System.Drawing.Size(39, 17);
            this.ctlLightLabel.TabIndex = 3;
            this.ctlLightLabel.Text = "Light";
            // 
            // ctlMediumLabel
            // 
            this.ctlMediumLabel.AutoSize = true;
            this.ctlMediumLabel.Location = new System.Drawing.Point(139, 145);
            this.ctlMediumLabel.Name = "ctlMediumLabel";
            this.ctlMediumLabel.Size = new System.Drawing.Size(57, 17);
            this.ctlMediumLabel.TabIndex = 4;
            this.ctlMediumLabel.Text = "Medium";
            // 
            // ctlFirmLabel
            // 
            this.ctlFirmLabel.AutoSize = true;
            this.ctlFirmLabel.Location = new System.Drawing.Point(245, 145);
            this.ctlFirmLabel.Name = "ctlFirmLabel";
            this.ctlFirmLabel.Size = new System.Drawing.Size(35, 17);
            this.ctlFirmLabel.TabIndex = 5;
            this.ctlFirmLabel.Text = "Firm";
            // 
            // ctlSilentClicking
            // 
            this.ctlSilentClicking.AutoSize = true;
            this.ctlSilentClicking.Location = new System.Drawing.Point(112, 68);
            this.ctlSilentClicking.Name = "ctlSilentClicking";
            this.ctlSilentClicking.Size = new System.Drawing.Size(115, 21);
            this.ctlSilentClicking.TabIndex = 1;
            this.ctlSilentClicking.Text = "Silent clicking";
            this.ctlSilentClicking.UseVisualStyleBackColor = true;
            // 
            // ctlMacOSClickOptions
            // 
            this.ctlMacOSClickOptions.AutoSize = true;
            this.ctlMacOSClickOptions.Location = new System.Drawing.Point(17, 21);
            this.ctlMacOSClickOptions.Name = "ctlMacOSClickOptions";
            this.ctlMacOSClickOptions.Size = new System.Drawing.Size(194, 21);
            this.ctlMacOSClickOptions.TabIndex = 0;
            this.ctlMacOSClickOptions.TabStop = true;
            this.ctlMacOSClickOptions.Text = "Use macOS Click Options:";
            this.ctlMacOSClickOptions.UseVisualStyleBackColor = true;
            this.ctlMacOSClickOptions.CheckedChanged += new System.EventHandler(this.ctlClickOptions_CheckedChanged);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.ctlLightLabel);
            this.groupBox1.Controls.Add(this.ctlMacOSClickOptions);
            this.groupBox1.Controls.Add(this.ctlSilentClicking);
            this.groupBox1.Controls.Add(this.ctlFirmLabel);
            this.groupBox1.Controls.Add(this.ctlMediumLabel);
            this.groupBox1.Controls.Add(this.ctlFeedback);
            this.groupBox1.Location = new System.Drawing.Point(12, 12);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(340, 194);
            this.groupBox1.TabIndex = 1;
            this.groupBox1.TabStop = false;
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.ctlMaximumFeedback);
            this.groupBox2.Controls.Add(this.ctlDisableFeedback);
            this.groupBox2.Location = new System.Drawing.Point(358, 12);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(445, 194);
            this.groupBox2.TabIndex = 2;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Click Options NOT available in macOS:";
            // 
            // ctlMaximumFeedback
            // 
            this.ctlMaximumFeedback.AutoSize = true;
            this.ctlMaximumFeedback.Location = new System.Drawing.Point(28, 98);
            this.ctlMaximumFeedback.Name = "ctlMaximumFeedback";
            this.ctlMaximumFeedback.Size = new System.Drawing.Size(332, 21);
            this.ctlMaximumFeedback.TabIndex = 1;
            this.ctlMaximumFeedback.TabStop = true;
            this.ctlMaximumFeedback.Text = "Maximum haptic feedback (very clicky and loud!)";
            this.ctlMaximumFeedback.UseVisualStyleBackColor = true;
            this.ctlMaximumFeedback.CheckedChanged += new System.EventHandler(this.ctlClickOptions_CheckedChanged);
            // 
            // ctlDisableFeedback
            // 
            this.ctlDisableFeedback.AutoSize = true;
            this.ctlDisableFeedback.Location = new System.Drawing.Point(28, 61);
            this.ctlDisableFeedback.Name = "ctlDisableFeedback";
            this.ctlDisableFeedback.Size = new System.Drawing.Size(398, 21);
            this.ctlDisableFeedback.TabIndex = 0;
            this.ctlDisableFeedback.TabStop = true;
            this.ctlDisableFeedback.Text = "Disable haptic feedback and force touch button completely";
            this.ctlDisableFeedback.UseVisualStyleBackColor = true;
            this.ctlDisableFeedback.CheckedChanged += new System.EventHandler(this.ctlClickOptions_CheckedChanged);
            // 
            // ctlFocusHack
            // 
            this.ctlFocusHack.Location = new System.Drawing.Point(-32, -32);
            this.ctlFocusHack.Name = "ctlFocusHack";
            this.ctlFocusHack.Size = new System.Drawing.Size(22, 22);
            this.ctlFocusHack.TabIndex = 10;
            this.ctlFocusHack.TabStop = false;
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.ctlStopSizeLabel);
            this.groupBox3.Controls.Add(this.ctlStopSizeValue);
            this.groupBox3.Controls.Add(this.ctlStopPressureLabel);
            this.groupBox3.Controls.Add(this.ctlStopPressureValue);
            this.groupBox3.Controls.Add(this.ctlStopSize);
            this.groupBox3.Controls.Add(this.ctlStopPressure);
            this.groupBox3.Controls.Add(this.ctlStopDoNothing);
            this.groupBox3.Location = new System.Drawing.Point(12, 212);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(791, 147);
            this.groupBox3.TabIndex = 3;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "When you lift your finger from the trackpad:";
            // 
            // ctlStopSizeLabel
            // 
            this.ctlStopSizeLabel.AutoSize = true;
            this.ctlStopSizeLabel.Location = new System.Drawing.Point(491, 108);
            this.ctlStopSizeLabel.Name = "ctlStopSizeLabel";
            this.ctlStopSizeLabel.Size = new System.Drawing.Size(164, 17);
            this.ctlStopSizeLabel.TabIndex = 6;
            this.ctlStopSizeLabel.Text = "units. (7 is a good value)";
            this.ctlStopSizeLabel.Click += new System.EventHandler(this.ctlStop_Click);
            // 
            // ctlStopSizeValue
            // 
            this.ctlStopSizeValue.Location = new System.Drawing.Point(434, 106);
            this.ctlStopSizeValue.Name = "ctlStopSizeValue";
            this.ctlStopSizeValue.Size = new System.Drawing.Size(51, 22);
            this.ctlStopSizeValue.TabIndex = 5;
            this.ctlStopSizeValue.Text = "7";
            this.ctlStopSizeValue.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // ctlStopPressureLabel
            // 
            this.ctlStopPressureLabel.AutoSize = true;
            this.ctlStopPressureLabel.Location = new System.Drawing.Point(419, 71);
            this.ctlStopPressureLabel.Name = "ctlStopPressureLabel";
            this.ctlStopPressureLabel.Size = new System.Drawing.Size(306, 17);
            this.ctlStopPressureLabel.TabIndex = 3;
            this.ctlStopPressureLabel.Text = "units. (0 means no pressure; 0 is a good value)";
            this.ctlStopPressureLabel.Click += new System.EventHandler(this.ctlStop_Click);
            // 
            // ctlStopPressureValue
            // 
            this.ctlStopPressureValue.Location = new System.Drawing.Point(362, 69);
            this.ctlStopPressureValue.Name = "ctlStopPressureValue";
            this.ctlStopPressureValue.Size = new System.Drawing.Size(51, 22);
            this.ctlStopPressureValue.TabIndex = 2;
            this.ctlStopPressureValue.Text = "0";
            this.ctlStopPressureValue.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // ctlStopSize
            // 
            this.ctlStopSize.AutoSize = true;
            this.ctlStopSize.Location = new System.Drawing.Point(17, 106);
            this.ctlStopSize.Name = "ctlStopSize";
            this.ctlStopSize.Size = new System.Drawing.Size(453, 21);
            this.ctlStopSize.TabIndex = 4;
            this.ctlStopSize.TabStop = true;
            this.ctlStopSize.Text = "Stop the pointer if the size of the touch area is less than or equal to";
            this.ctlStopSize.UseVisualStyleBackColor = true;
            this.ctlStopSize.CheckedChanged += new System.EventHandler(this.ctlStop_CheckedChanged);
            // 
            // ctlStopPressure
            // 
            this.ctlStopPressure.AutoSize = true;
            this.ctlStopPressure.Location = new System.Drawing.Point(17, 69);
            this.ctlStopPressure.Name = "ctlStopPressure";
            this.ctlStopPressure.Size = new System.Drawing.Size(372, 21);
            this.ctlStopPressure.TabIndex = 1;
            this.ctlStopPressure.TabStop = true;
            this.ctlStopPressure.Text = "Stop the pointer if the pressure is less than or equal to";
            this.ctlStopPressure.UseVisualStyleBackColor = true;
            this.ctlStopPressure.CheckedChanged += new System.EventHandler(this.ctlStop_CheckedChanged);
            // 
            // ctlStopDoNothing
            // 
            this.ctlStopDoNothing.AutoSize = true;
            this.ctlStopDoNothing.Location = new System.Drawing.Point(17, 32);
            this.ctlStopDoNothing.Name = "ctlStopDoNothing";
            this.ctlStopDoNothing.Size = new System.Drawing.Size(98, 21);
            this.ctlStopDoNothing.TabIndex = 0;
            this.ctlStopDoNothing.TabStop = true;
            this.ctlStopDoNothing.Text = "Do nothing";
            this.ctlStopDoNothing.UseVisualStyleBackColor = true;
            this.ctlStopDoNothing.CheckedChanged += new System.EventHandler(this.ctlStop_CheckedChanged);
            // 
            // groupBox4
            // 
            this.groupBox4.Controls.Add(this.ctlPalmRejection);
            this.groupBox4.Controls.Add(this.ctlIgnoreButtonFinger);
            this.groupBox4.Controls.Add(this.ctlIgnoreNearFingers);
            this.groupBox4.Location = new System.Drawing.Point(12, 365);
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.Size = new System.Drawing.Size(791, 147);
            this.groupBox4.TabIndex = 4;
            this.groupBox4.TabStop = false;
            this.groupBox4.Text = "Other options:";
            // 
            // ctlIgnoreNearFingers
            // 
            this.ctlIgnoreNearFingers.AutoSize = true;
            this.ctlIgnoreNearFingers.Location = new System.Drawing.Point(17, 32);
            this.ctlIgnoreNearFingers.Name = "ctlIgnoreNearFingers";
            this.ctlIgnoreNearFingers.Size = new System.Drawing.Size(400, 21);
            this.ctlIgnoreNearFingers.TabIndex = 0;
            this.ctlIgnoreNearFingers.Text = "Ignore input from fingers not touching the trackpad surface";
            this.ctlIgnoreNearFingers.UseVisualStyleBackColor = true;
            // 
            // ctlIgnoreButtonFinger
            // 
            this.ctlIgnoreButtonFinger.Location = new System.Drawing.Point(17, 58);
            this.ctlIgnoreButtonFinger.Name = "ctlIgnoreButtonFinger";
            this.ctlIgnoreButtonFinger.Size = new System.Drawing.Size(755, 41);
            this.ctlIgnoreButtonFinger.TabIndex = 1;
            this.ctlIgnoreButtonFinger.Text = "Ignore input from the finger used to click the force touch button (useful for dra" +
    "gging, if you use your thumb to click the button and your index finger to move t" +
    "he pointer, for example)";
            this.ctlIgnoreButtonFinger.UseVisualStyleBackColor = true;
            // 
            // ctlPalmRejection
            // 
            this.ctlPalmRejection.AutoSize = true;
            this.ctlPalmRejection.Location = new System.Drawing.Point(17, 106);
            this.ctlPalmRejection.Name = "ctlPalmRejection";
            this.ctlPalmRejection.Size = new System.Drawing.Size(124, 21);
            this.ctlPalmRejection.TabIndex = 2;
            this.ctlPalmRejection.Text = "Palm Rejection";
            this.ctlPalmRejection.UseVisualStyleBackColor = true;
            // 
            // Main
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(815, 582);
            this.Controls.Add(this.groupBox4);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.ctlFocusHack);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.ctlApply);
            this.Controls.Add(this.ctlInstallDriver);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.Name = "Main";
            this.Text = "Magic Trackpad 2 Control Panel";
            this.Load += new System.EventHandler(this.Main_Load);
            ((System.ComponentModel.ISupportInitialize)(this.ctlFeedback)).EndInit();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            this.groupBox4.ResumeLayout(false);
            this.groupBox4.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button ctlInstallDriver;
        private System.Windows.Forms.Button ctlApply;
        private System.Windows.Forms.TrackBar ctlFeedback;
        private System.Windows.Forms.Label ctlLightLabel;
        private System.Windows.Forms.Label ctlMediumLabel;
        private System.Windows.Forms.Label ctlFirmLabel;
        private System.Windows.Forms.CheckBox ctlSilentClicking;
        private System.Windows.Forms.RadioButton ctlMacOSClickOptions;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.RadioButton ctlMaximumFeedback;
        private System.Windows.Forms.RadioButton ctlDisableFeedback;
        private System.Windows.Forms.TextBox ctlFocusHack;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.Label ctlStopSizeLabel;
        private System.Windows.Forms.TextBox ctlStopSizeValue;
        private System.Windows.Forms.Label ctlStopPressureLabel;
        private System.Windows.Forms.TextBox ctlStopPressureValue;
        private System.Windows.Forms.RadioButton ctlStopSize;
        private System.Windows.Forms.RadioButton ctlStopPressure;
        private System.Windows.Forms.RadioButton ctlStopDoNothing;
        private System.Windows.Forms.GroupBox groupBox4;
        private System.Windows.Forms.CheckBox ctlIgnoreButtonFinger;
        private System.Windows.Forms.CheckBox ctlIgnoreNearFingers;
        private System.Windows.Forms.CheckBox ctlPalmRejection;
    }
}

