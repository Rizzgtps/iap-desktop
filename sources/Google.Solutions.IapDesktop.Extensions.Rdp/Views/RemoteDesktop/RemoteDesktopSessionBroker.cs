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
using Google.Solutions.IapDesktop.Application.ObjectModel;
using Google.Solutions.IapDesktop.Application.Services.Integration;
using Google.Solutions.IapDesktop.Application.Views;
using Google.Solutions.IapDesktop.Extensions.Rdp.Services.Connection;
using System;

namespace Google.Solutions.IapDesktop.Extensions.Rdp.Views.RemoteDesktop
{
    public interface IRemoteDesktopSession
    {
        void Close();

        bool TrySetFullscreen(FullScreenMode mode);

        bool IsConnected { get; }

        void ShowSecurityScreen();

        void ShowTaskManager();
    }

    public enum FullScreenMode
    {
        Off,
        SingleScreen,
        AllScreens
    }

    public interface IRemoteDesktopSessionBroker : ISessionBroker
    {
        IRemoteDesktopSession ActiveSession { get; }

        IRemoteDesktopSession Connect(
            InstanceLocator vmInstance,
            string server,
            ushort port,
            RdpInstanceSettings settings);
    }

    [Service(typeof(IRemoteDesktopSessionBroker), ServiceLifetime.Singleton, ServiceVisibility.Global)]
    [ServiceCategory(typeof(ISessionBroker))]
    public class RemoteDesktopSessionBroker : IRemoteDesktopSessionBroker
    {
        private readonly IServiceProvider serviceProvider;
        private readonly IMainForm mainForm;

        public RemoteDesktopSessionBroker(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
            this.mainForm = serviceProvider.GetService<IMainForm>();

            // NB. The ServiceCategory attribute causes this class to be 
            // announced to the session connection broker.
        }

        public IRemoteDesktopSession ActiveSession
        {
            get => RemoteDesktopPane.TryGetActivePane(this.mainForm);
        }

        public bool IsConnected(InstanceLocator vmInstance)
        {
            return RemoteDesktopPane.TryGetExistingPane(
                this.mainForm,
                vmInstance) != null;
        }

        public bool TryActivate(InstanceLocator vmInstance)
        {
            // Check if there is an existing session/pane.
            var rdpPane = RemoteDesktopPane.TryGetExistingPane(
                this.mainForm,
                vmInstance);
            if (rdpPane != null)
            {
                // Pane found, activate.
                rdpPane.ShowWindow();
                return true;
            }
            else
            {
                return false;
            }
        }

        public IRemoteDesktopSession Connect(
            InstanceLocator vmInstance,
            string server,
            ushort port,
            RdpInstanceSettings settings)
        {
            var rdpPane = new RemoteDesktopPane(
                this.serviceProvider,
                vmInstance);
            rdpPane.ShowWindow();
            rdpPane.Connect(server, port, settings);

            return rdpPane;
        }
    }
}
