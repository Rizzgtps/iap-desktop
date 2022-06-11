﻿//
// Copyright 2020 Google LLC
//
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
//

using Google.Solutions.Common.Locator;
using Google.Solutions.Common.Util;
using Google.Solutions.IapDesktop.Application.ObjectModel;
using Google.Solutions.IapDesktop.Application.Services.Integration;
using Google.Solutions.IapDesktop.Application.Util;
using Google.Solutions.IapDesktop.Application.Views;
using Google.Solutions.IapDesktop.Application.Views.Dialog;
using Google.Solutions.IapDesktop.Extensions.Shell.Controls;
using Google.Solutions.IapDesktop.Extensions.Shell.Services.Settings;
using Google.Solutions.IapDesktop.Extensions.Shell.Services.Ssh;
using Google.Solutions.Ssh;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

#pragma warning disable CA1031 // Do not catch general exception types

namespace Google.Solutions.IapDesktop.Extensions.Shell.Views.SshTerminal
{
    public partial class TerminalPaneBase : DocumentWindow
    {
        private readonly IExceptionDialog exceptionDialog;

#pragma warning disable IDE0069 // Disposable fields should be disposed
        protected TerminalPaneViewModelBase ViewModel { get; }
#pragma warning restore IDE0069 // Disposable fields should be disposed

        //---------------------------------------------------------------------
        // Properties.
        //---------------------------------------------------------------------

        public VirtualTerminal Terminal => this.terminal;

        public InstanceLocator Instance => this.ViewModel.Instance;

        public bool IsFormClosing { get; private set; } = false;

        public override string Text
        {
            get => this.ViewModel?.Instance?.Name ?? "Terminal";
            set { }
        }

        //---------------------------------------------------------------------
        // Ctor.
        //---------------------------------------------------------------------

        public TerminalPaneBase()
        {
            // Constructor is for designer only.
        }

        internal TerminalPaneBase(
            IServiceProvider serviceProvider,
            TerminalPaneViewModelBase viewModel)
            : base(serviceProvider)
        {
            InitializeComponent();

            this.exceptionDialog = serviceProvider.GetService<IExceptionDialog>();
            this.ViewModel = viewModel;

            viewModel.View = this;
            Debug.Assert(this.ViewModel.View != null);

            //
            // Bind controls.
            //
            this.reconnectPanel.BindReadonlyProperty(
                c => c.Visible,
                this.ViewModel,
                m => m.IsReconnectPanelVisible,
                this.components);
            this.spinner.BindReadonlyProperty(
                c => c.Visible,
                this.ViewModel,
                m => m.IsSpinnerVisible,
                this.components);
            this.terminal.BindReadonlyProperty(
                c => c.Visible,
                this.ViewModel,
                m => m.IsTerminalVisible,
                this.components);
            this.ViewModel.OnPropertyChange(
                m => m.IsTerminalVisible,
                visible =>
                {
                    if (visible)
                    {
                        this.terminal.Focus();
                    }
                });

            Debug.Assert(this.Text != this.Name);

            //
            // Terminal I/O.
            //
            this.Terminal.SendData += (sender, args) =>
            {
                //
                // Relay user input to connection.
                //
                // NB. This method will never throw an exception, so it is ok
                // to fire-and-forget it.
                //
                OnInputReceivedFromUserAsync(args.Data)
                    .ContinueWith(_ => { });
            };

            this.Terminal.TerminalResized += (sender, args) =>
            {
                //
                // NB. This method will never throw an exception, so it is ok
                // to fire-and-forget it.
                //
                OnTerminalResizedByUser()
                    .ContinueWith(_ => { });
            };

            this.Terminal.WindowTitleChanged += (sender, args) =>
            {
                this.TabText = this.Terminal.WindowTitle;
            };

            this.ViewModel.ConnectionFailed += OnErrorReceivedFromServerAsync;
            this.ViewModel.DataReceived += OnDataReceivedFromServerAsync;

            //
            // Apply Terminal settings.
            //
            // Automatically reapply when settings change.
            //
            var terminalSettingsRepository = serviceProvider.GetService<TerminalSettingsRepository>();
            var settings = terminalSettingsRepository.GetSettings();
            ApplyTerminalSettings(settings);

            void reapplyTerminalSettings(object s, EventArgs<TerminalSettings> e)
                => ApplyTerminalSettings(e.Data);

            terminalSettingsRepository.SettingsChanged += reapplyTerminalSettings;

            //
            // Disposing.
            //
            this.Disposed += (sender, args) =>
            {
                if (!this.IsDisposed)
                {
                    this.ViewModel.ConnectionFailed -= OnErrorReceivedFromServerAsync;
                    this.ViewModel.DataReceived -= OnDataReceivedFromServerAsync;
                    this.ViewModel.Dispose();

                    terminalSettingsRepository.SettingsChanged -= reapplyTerminalSettings;
                }
            };
            this.FormClosed += OnFormClosed;

            //
            // Add debug commands, but only show it if Shift key is pressed.
            //
            var copyReceivedData = new ToolStripMenuItem("Copy received data");
            copyReceivedData.Click += (sender, args) => this.ViewModel.CopyReceivedDataToClipboard();
            this.TabContextStrip.Opening += (sender, args) =>
            {
                copyReceivedData.Visible = Control.ModifierKeys.HasFlag(Keys.Shift);
            };
            this.TabContextStrip.Items.Add(copyReceivedData);

            var copySentData = new ToolStripMenuItem("Copy sent data");
            copySentData.Click += (sender, args) => this.ViewModel.CopySentDataToClipboard();
            this.TabContextStrip.Opening += (sender, args) =>
            {
                copySentData.Visible = Control.ModifierKeys.HasFlag(Keys.Shift);
            };
            this.TabContextStrip.Items.Add(copySentData);
        }

        private void ApplyTerminalSettings(TerminalSettings settings)
        {
            this.terminal.EnableCtrlC = settings.IsCopyPasteUsingCtrlCAndCtrlVEnabled.BoolValue;
            this.terminal.EnableCtrlV = settings.IsCopyPasteUsingCtrlCAndCtrlVEnabled.BoolValue;

            this.terminal.EnableCtrlInsert = settings.IsCopyPasteUsingShiftInsertAndCtrlInsertEnabled.BoolValue;
            this.terminal.EnableShiftInsert = settings.IsCopyPasteUsingShiftInsertAndCtrlInsertEnabled.BoolValue;

            this.terminal.EnableTypographicQuoteConversionOnPaste = settings.IsQuoteConvertionOnPasteEnabled.BoolValue;

            this.terminal.EnableCtrlA = settings.IsSelectAllUsingCtrlAEnabled.BoolValue;
            this.terminal.EnableShiftLeftRight = settings.IsSelectUsingShiftArrrowEnabled.BoolValue;
            this.terminal.EnableShiftUpDown = settings.IsSelectUsingShiftArrrowEnabled.BoolValue;

            this.terminal.EnableCtrlLeftRight = settings.IsNavigationUsingControlArrrowEnabled.BoolValue;

            this.terminal.EnableCtrlUpDown = settings.IsScrollingUsingCtrlUpDownEnabled.BoolValue;
            this.terminal.EnableCtrlHomeEnd = settings.IsScrollingUsingCtrlHomeEndEnabled.BoolValue;

            this.terminal.TerminalFont = new TerminalFont(
                settings.FontFamily.StringValue,
                TerminalSettings.FontSizeFromDword(settings.FontSizeAsDword.IntValue));
        }

        //---------------------------------------------------------------------
        // Event handlers
        //---------------------------------------------------------------------

        private void OnLayout(object sender, LayoutEventArgs e)
        {
            this.spinner.Location = new Point(
                (this.Size.Width - this.spinner.Width) / 2,
                (this.Size.Height - this.spinner.Height) / 2);

            this.reconnectPanel.Location = new Point(
                (this.Size.Width - this.reconnectPanel.Width) / 2,
                (this.Size.Height - this.reconnectPanel.Height) / 2);
        }

        private void ShowErrorAndClose(string caption, Exception e)
        {
            Debug.Assert(!this.InvokeRequired);

            if (!e.IsCancellation())
            {
                this.exceptionDialog.Show(this, caption, e);
            }

            Close();
        }

        private void OnFormClosed(object sender, System.Windows.Forms.FormClosedEventArgs e)
        {
            // Mark this pane as being in closing state even though it is still
            // visible at this point. The flag ensures that this pane is
            // not considered by TryGetExistingPane anymore.
            this.IsFormClosing = true;

            this.ViewModel.DisconnectAsync().Wait();
        }

        private async Task OnInputReceivedFromUserAsync(string input)
        {
            Debug.Assert(!this.InvokeRequired);

            try
            {
                await this.ViewModel.SendAsync(input)
                    .ConfigureAwait(true);
            }
            catch (Exception e)
            {
                ShowErrorAndClose("Sending data failed", e);
            }
        }

        private async Task OnTerminalResizedByUser()
        {
            Debug.Assert(!this.InvokeRequired);

            try
            {
                await this.ViewModel.ResizeTerminal(
                    new TerminalSize(
                        (ushort)this.Terminal.Columns,
                        (ushort)this.Terminal.Rows))
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                ShowErrorAndClose("Sending data failed", e);
            }
        }

        private void OnDataReceivedFromServerAsync(
            object sender,
            DataEventArgs args)
        {
            Debug.Assert(!this.InvokeRequired);

            if (args.Data.Length == 0)
            {
                // End of stream -> close the pane.
                Close();
            }
            else
            {
                this.Terminal.ReceiveData(args.Data);
            }
        }

        private void OnErrorReceivedFromServerAsync(
            object sender,
            ConnectionErrorEventArgs args)
        {
            Debug.Assert(!this.InvokeRequired);
            ShowErrorAndClose("Connection terminated", args.Error);
        }

        private async void OnReconnectLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            await ConnectAsync()
                .ConfigureAwait(false);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyData == ToggleFocusHotKey)
            {
                //
                // Release focus and move it to the panel, which ensures
                // that any other shortcuts start applying again.
                //
                this.MainForm.MainPanel.Focus();
                e.Handled = true;
            }
        }

        //---------------------------------------------------------------------
        // Actions
        //---------------------------------------------------------------------

        public async Task ConnectAsync()
        {
            try
            {
                await this.ViewModel.ConnectAsync(
                        new TerminalSize(
                            (ushort)this.terminal.Columns,
                            (ushort)this.terminal.Rows))
                    .ConfigureAwait(true);
            }
            catch (Exception e)
            {
                ShowErrorAndClose("Connection failed", e);
            }
        }

        public Task SendAsync(string command)
            => this.ViewModel.SendAsync(command);

        public Task DisconnectAsync()
            => this.ViewModel.DisconnectAsync();

        //---------------------------------------------------------------------
        // ISession
        //---------------------------------------------------------------------

        public bool IsConnected =>
            this.ViewModel.ConnectionStatus == TerminalPaneViewModelBase.Status.Connected;
    }
}