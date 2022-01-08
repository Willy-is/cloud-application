using System;
using System.Windows.Forms;
using System.Collections.Generic;
using System.IO;
using System.Net;
using AT1;
using System.Threading;
using System.ServiceModel;

namespace AllocationsApplication
{
    partial class AllocationsViewerForm : Form
    {
        #region properties
        private Allocations AT1Allocations;
        private Configuration AT1Configuration;
        private ErrorsViewer ErrorListViewer = new ErrorsViewer();
        private AboutBox AboutBox = new AboutBox();

        AutoResetEvent autoResetEvent = new AutoResetEvent(false);
        List<String> SetsOfAllocations;
        int WcfsCalls;
        int WcfsCallsCompleted;
        int WcfsCallsTimedout;

        int Deadline = 300000;
        
        readonly object AllocationsLock = new object();
        #endregion

        #region constructors
        public AllocationsViewerForm()
        {
            InitializeComponent();
        }
        #endregion

        #region File menu event handlers
        private void OpenAllocationsFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ClearGUI();

            // Process allocations and configuration files.
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                // Get both filenames.
                String allocationsFileName = openFileDialog1.FileName;
                String configurationFileName = Allocations.ConfigurationFileName(allocationsFileName);

                // Parse configuration file.
                if (configurationFileName == null)
                    AT1Configuration = new Configuration();
                else
                {
                    using (WebClient configurationWebClient = new WebClient())
                    using (Stream configurationStream = configurationWebClient.OpenRead(configurationFileName))
                    using (StreamReader configurationFile = new StreamReader(configurationStream))
                    {
                        Configuration.TryParse(configurationFile, configurationFileName, out AT1Configuration, out List<String> configurationErrors);
                    }
                }

                // Parse Allocations file.
                using (StreamReader allocationsFile = new StreamReader(allocationsFileName))
                {
                    Allocations.TryParse(allocationsFile, allocationsFileName, AT1Configuration, out AT1Allocations, out List<String> allocationsErrors);
                }

                // Refesh GUI and Log errors.
                UpdateGUI();
                AT1Allocations.LogFileErrors(AT1Allocations.FileErrorsTXT);
                AT1Allocations.LogFileErrors(AT1Configuration.FileErrorsTXT);
            }
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }
        #endregion

        #region  Clear and Update GUI
        private void ClearGUI()
        {
            // As we are opening a Configuration file,
            // indicate allocations are not valid, and clear GUI.
            allocationToolStripMenuItem.Enabled = false;

            if (allocationsWebBrowser.Document != null)
                allocationsWebBrowser.Document.OpenNew(true);
            allocationsWebBrowser.DocumentText = String.Empty;

            if (ErrorListViewer.WebBrowser.Document != null)
                ErrorListViewer.WebBrowser.Document.OpenNew(true);
            ErrorListViewer.WebBrowser.DocumentText = String.Empty;
        }

        private void UpdateGUI()
        {
            if (AT1Allocations != null && AT1Allocations.FileValid &&
                AT1Configuration != null && AT1Configuration.FileValid)
                allocationToolStripMenuItem.Enabled = true;

            if (allocationsWebBrowser.Document != null)
                allocationsWebBrowser.Document.OpenNew(true);
            if (ErrorListViewer.WebBrowser.Document != null)
                ErrorListViewer.WebBrowser.Document.OpenNew(true);

            if (AT1Allocations != null)
            {
                allocationsWebBrowser.DocumentText = AT1Allocations.ToStringHTML();

                ErrorListViewer.WebBrowser.DocumentText =
                    AT1Allocations.FileErrorsHTML +
                    AT1Configuration.FileErrorsHTML +
                    AT1Allocations.AllocationsErrorsHTML;
            }
            else
            {
                if (WcfsCallsTimedout > 0 && WcfsCallsTimedout == WcfsCalls)
                    allocationsWebBrowser.DocumentText = "<p>All operations timed out. There are no allocations.</p>";
                else
                    allocationsWebBrowser.DocumentText = "<p>There are no allocations.</p>";
                ErrorListViewer.WebBrowser.DocumentText = AT1Configuration.FileErrorsHTML;
            }
        }
        #endregion

        #region Validate menu event handlers
        private void AllocationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Check if the allocations are valid.
            AT1Allocations.Validate();

            // Refesh GUI and Log errors.
            UpdateGUI();
            AT1Allocations.LogFileErrors(AT1Allocations.AllocationsErrorsTXT);
        }
        #endregion

        #region View menu event handlers
        private void ErrorListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ErrorListViewer != null)
            {
                ErrorListViewer.WindowState = FormWindowState.Normal;
                ErrorListViewer.Show();
                ErrorListViewer.Activate();
            }
        }
        #endregion

        #region Help menu event handlers
        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutBox.ShowDialog();
        }
        #endregion

        #region Generate Allocations

        private void generateAllocationsButton_Click(object sender, EventArgs e)
        {
            ClearGUI();

            String configurationFileName = urlComboBox.Text;
            using (WebClient configurationWebClient = new WebClient())
            using (Stream configurationStream = configurationWebClient.OpenRead(configurationFileName))
            using (StreamReader configurationFile = new StreamReader(configurationStream))
            {
                Configuration.TryParse(configurationFile, configurationFileName, out AT1Configuration, out List<String> configurationErrors);
            }

            // Get sets of allocations from WCF Services.
            System.Threading.Tasks.Task.Run(() => WcfsGenerateAllocations(configurationFileName));

            // Wait 5 minutes for all WCF calls to return in the server Thread.
            autoResetEvent.WaitOne(Deadline);

            // Determine lowest energy and display it.
            Double lowestEnergy = Double.MaxValue;

            foreach (String allocation in SetsOfAllocations)
            {
                Allocations.TryParse(allocation, AT1Configuration, out Allocations allocations, out List<String> allocationsErrors);

                if (allocations.Count > 0)
                {
                    if (allocations[0].Energy() < lowestEnergy)
                    {
                        lowestEnergy = allocations[0].Energy();
                        AT1Allocations = allocations;
                    }
                }
            }

            UpdateGUI();
        }

        private void WcfsGenerateAllocations(String configurationFileName)
        {
            try
            {
                using (AwsNano.ServiceClient awsNano = new AwsNano.ServiceClient())
                using (AwsMicro.ServiceClient awsMicro = new AwsMicro.ServiceClient())
                using (AwsSmall.ServiceClient awsSmall = new AwsSmall.ServiceClient())
                {
                    awsNano.GenerateAllocationsCompleted += AwsNano_GenerateAllocationsCompleted;
                    awsMicro.GenerateAllocationsCompleted += AwsMicro_GenerateAllocationsCompleted;
                    awsSmall.GenerateAllocationsCompleted += AwsSmall_GenerateAllocationsCompleted;

                    lock (AllocationsLock)
                    {
                        WcfsCalls = 18;
                        WcfsCallsCompleted = 0;
                        WcfsCallsTimedout = 0;
                        SetsOfAllocations = new List<String>();
                    }
                    
                    for(int i = 0; i < 6; i++) {
                        awsNano.GenerateAllocationsAsync(configurationFileName);
                        awsMicro.GenerateAllocationsAsync(configurationFileName);
                        awsSmall.GenerateAllocationsAsync(configurationFileName);
                    }
                }
            }
            catch (WebException webEx)
            {
                MessageBox.Show("Web error: " + webEx.Message);
            }
            catch (Exception ex)
            {
                MessageBox.Show("General error: " + ex.Message);
            }
        }

        private void AwsSmall_GenerateAllocationsCompleted(object sender, AwsSmall.GenerateAllocationsCompletedEventArgs e)
        {
            try
            {
                WcfsCompleted(SetsOfAllocations, e.Result);
            }
            catch (Exception ex) when (ex.InnerException is TimeoutException tex)
            {
                WcfsCallsTimedout++;
                WcfsCompleted();
            }
            catch (Exception ex) when (ex.InnerException is FaultException fex)
            {
                if (fex.Message.Equals("WCF Service timed out"))
                    WcfsCallsTimedout++;
                WcfsCompleted();
            }
            catch (Exception)
            {
                WcfsCompleted();
            }
        }

        private void AwsMicro_GenerateAllocationsCompleted(object sender, AwsMicro.GenerateAllocationsCompletedEventArgs e)
        {
            try
            {
                WcfsCompleted(SetsOfAllocations, e.Result);
            }
            catch (Exception ex) when (ex.InnerException is TimeoutException tex)
            {
                WcfsCallsTimedout++;
                WcfsCompleted();
            }
            catch (Exception ex) when (ex.InnerException is FaultException fex)
            {
                if (fex.Message.Equals("WCF Service timed out"))
                    WcfsCallsTimedout++;
                WcfsCompleted();
            }
            catch (Exception)
            {
                WcfsCompleted();
            }
        }

        private void AwsNano_GenerateAllocationsCompleted(object sender, AwsNano.GenerateAllocationsCompletedEventArgs e)
        {
            try
            {
                WcfsCompleted(SetsOfAllocations, e.Result);
            }
            catch (Exception ex) when (ex.InnerException is TimeoutException tex)
            {
                WcfsCallsTimedout++;
                WcfsCompleted();
            }
            catch (Exception ex) when (ex.InnerException is FaultException fex)
            {
                if (fex.Message.Equals("WCF Service timed out"))
                    WcfsCallsTimedout++;
                WcfsCompleted();
            }
            catch (Exception)
            {
                WcfsCompleted();
            }
        }

        private void WcfsCompleted()
        {
            lock (AllocationsLock)
            {
                WcfsCallsCompleted++;
                if (WcfsCallsCompleted == WcfsCalls)
                    autoResetEvent.Set();
            }
        }

        private void WcfsCompleted(List<String> list, String allocations)
        {
            lock (AllocationsLock)
            {
                WcfsCallsCompleted++;
                list.Add(allocations);
                if (WcfsCallsCompleted == WcfsCalls)
                    autoResetEvent.Set();
            }
        }

        private void LocalWcfs_GenerateAllocationsCompleted(object sender, LocalWcfs.GenerateAllocationsCompletedEventArgs e)
        {
            try
            {
                WcfsCompleted(SetsOfAllocations, e.Result);
            }
            catch (Exception ex) when (ex.InnerException is TimeoutException tex)
            {
                // Local timeout for "All operations timed out message".
                WcfsCallsTimedout++;
                WcfsCompleted();
            }
            catch (Exception ex) when (ex.InnerException is FaultException fex)
            {
                // Remote timout.
                WcfsCallsTimedout++;
                WcfsCompleted();
            }
            catch (Exception)
            {
                WcfsCompleted();
            }
        }
        #endregion

    }
}
