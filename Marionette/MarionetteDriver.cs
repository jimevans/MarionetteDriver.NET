// -----------------------------------------------------------------------
// <copyright file="MarionetteDriver.cs" company="">
// TODO: Update copyright text.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using OpenQA.Selenium.Remote;

namespace OpenQA.Selenium.Firefox
{
    /// <summary>
    /// TODO: Update summary.
    /// </summary>
    public class MarionetteDriver : RemoteWebDriver
    {
        private const string binaryLocation = @"C:\Program Files (x86)\Nightly\Firefox.exe";
        private const int defaultPort = 2828;

        public MarionetteDriver()
            : this(new FirefoxBinary(binaryLocation), new FirefoxProfile(), defaultPort)
        {
        }

        public MarionetteDriver(FirefoxBinary binary, FirefoxProfile profile, int port)
            : base(CreateCommandExecutor(binary, profile, port), DesiredCapabilities.Firefox())
        {
        }

        private static ICommandExecutor CreateCommandExecutor(FirefoxBinary binary, FirefoxProfile profile, int port)
        {
            profile.SetPreference("marionette.defaultPrefs.enabled", true);
            profile.SetPreference("marionette.defaultPrefs.port", port);
            profile.SetPreference("browser.warnOnQuit", false);
            return new MarionetteCommandExecutor(binary, profile);
        }

        protected override void StartClient()
        {
            MarionetteCommandExecutor executor = this.CommandExecutor as MarionetteCommandExecutor;
            if (executor == null)
            {
                throw new WebDriverException("command executor not set. This is bad. Very bad.");
            }

            executor.Start();
        }

        protected override void StopClient()
        {
            MarionetteCommandExecutor executor = this.CommandExecutor as MarionetteCommandExecutor;
            if (executor == null)
            {
                throw new WebDriverException("command executor not set. This is bad. Very bad.");
            }

            executor.Quit();
        }
    }
}
