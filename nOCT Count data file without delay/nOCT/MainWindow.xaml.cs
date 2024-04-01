using NationalInstruments.Analysis;
using NationalInstruments.Analysis.Conversion;
using NationalInstruments.Analysis.Dsp;
using NationalInstruments.Analysis.Dsp.Filters;
using NationalInstruments.Analysis.Math;
using NationalInstruments.Analysis.Monitoring;
using NationalInstruments.Analysis.SignalGeneration;
using NationalInstruments.Analysis.SpectralMeasurements;
using NationalInstruments;
using NationalInstruments.UI;
using NationalInstruments.DAQmx;
/// using NationalInstruments.NI4882;
using NationalInstruments.UI.WindowsForms;
using NationalInstruments.Controls;
using NationalInstruments.Controls.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using System.Threading;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;


namespace nOCT
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>

    public partial class MainWindow : Window
    {
        private CUIData UIData; // user interface data
        LinkedList<CDataNode> nodeList = new LinkedList<CDataNode>();
        LinkedList<CDataNode> writeList = new LinkedList<CDataNode>();     // FIFO node list to be written to disk, pushed in SaveThread, poped in CleanupThread
        private CThreadData ThreadData;
        DispatcherTimer timerUIUpdate = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
        
        public MainWindow()
        {
            InitializeComponent();

            #region initialization (UIData, ThreadData)
            UIData = new CUIData { };
            this.DataContext = UIData;
            UIData.nDiagnosticsNodeID = -1;
            ThreadData = new CThreadData();
            #endregion

            #region load last parameter file (if it exists)
            if (File.Exists("lastparameterfilename.txt"))
            {
                string line;
                StreamReader lastParameter = new StreamReader("lastparameterfilename.txt");
                line = lastParameter.ReadLine();
                lastParameter.Close();
                if (File.Exists(line))
                {
                    UIData.strConfigurationParameterFilename = line;
                    OpenParameterFile();
                }   // if (File.Exists(line
            }   // if (File.Exists("lastparameterfilename.txt"
            #endregion

            #region start update timer
            timerUIUpdate.Tick += new EventHandler(UIUpdate);
            timerUIUpdate.IsEnabled = true;
            #endregion

        }   // public MainWindow

        private void btnConfigurationStart_Click(object sender, RoutedEventArgs e)
        {
            #region initialize data node linked list

            int nLineLength = 1;
            if ((UIData.bConfigurationIMAQDevice1 || UIData.bConfigurationIMAQDevice2) && !(UIData.bConfigurationHSDevice))
            {
                nLineLength = UIData.nConfigurationIMAQLineLength;
                UIData.nOperationSlowGalvoCurrentFrame = nLineLength;
            }
            bool bChannel1 = (UIData.bConfigurationIMAQDevice1) || (UIData.bConfigurationHSDevice && UIData.bConfigurationHSChannel1);
            bool bChannel2 = (UIData.bConfigurationIMAQDevice2) || (UIData.bConfigurationHSDevice && UIData.bConfigurationHSChannel2);
            CDataNode datanode;
            for (int nID = 0; nID < UIData.nConfigurationLinkedListLength; nID++)
            {
                datanode = new CDataNode(nID, nLineLength, UIData.nConfigurationChunksPerImage, UIData.nConfigurationLinesPerChunk, bChannel1, bChannel2);
                nodeList.AddLast(datanode);
            }   // for (int nID
            //set diagnostics node to first in list
            UIData.nodeDiagnostics = nodeList.First;

            #endregion

            #region initialize threads

            // start all threads
            ThreadData.threadPrimaryKernel = new Thread(PrimaryKernel);
            ThreadData.threadAcquisition = new Thread(AcquisitionThread);
            ThreadData.threadWFM = new Thread(WFMThread);
            ThreadData.threadSave = new Thread(SaveThread);
            ThreadData.threadWrite = new Thread(WriteThread);
            ThreadData.threadScan = new Thread(ScanThread);
            ThreadData.threadProcessing = new Thread(ProcessingThread);
            ThreadData.threadCleanup = new Thread(CleanupThread);
            ThreadData.Initialize(UIData.nConfigurationLinkedListLength - 1, UIData.nConfigurationFramesInWFM - 1);

            // wait for them to be ready
            ThreadData.threadPrimaryKernel.Start();
            
            ThreadData.mrwePrimaryKernelReady.WaitOne();

            #endregion

            // initialize graphs

            #region value initialization

            int nNumberChunksPerFrame = UIData.nConfigurationPreIgnoreChunks + UIData.nConfigurationChunksPerImage + UIData.nConfigurationPostIgnoreChunks;
            int nNumberLines = UIData.nConfigurationChunksPerImage * UIData.nConfigurationLinesPerChunk;
            int nDepthProfileLength = UIData.nConfigurationIMAQLineLength / 2;
            int nNumberFrames = UIData.nConfigurationImagesPerVolume;

            #endregion

            #region diagnostic graphs

            UIData.pnGraphDiagnosticsLinkedList = new int[UIData.nConfigurationLinkedListLength, 5];
            graphDiagnosticsNodeList.DataSource = UIData.pnGraphDiagnosticsLinkedList;

            // ThreadData.pdWFMInMemory initialized in WFMThread
            graphDiagnosticsWFM.DataSource = ThreadData.pdWFMInMemory;
            int nTicksPerFrame = (nNumberChunksPerFrame) * (UIData.nConfigurationLinesPerChunk * UIData.nConfigurationTicksPerLine);
            axisDiagnosticsWFMHorizontal.Range = new Range<double>(0, UIData.nConfigurationFramesInWFM * nTicksPerFrame);
            axisDiagnosticsWFMVertical.Range = new Range<double>(-5.0, 5.0);

            #endregion

            #region processing graphs

            UIData.pdGraphDAQ = new double[4, UIData.nConfigurationLinesPerChunk * UIData.nConfigurationChunksPerImage];
            graphDAQ.DataSource = UIData.pdGraphDAQ;
            axisDAQHorizontal.Range = new Range<double>(0, UIData.nConfigurationLinesPerChunk * UIData.nConfigurationChunksPerImage);
            axisDAQVertical.Range = new Range<double>(-5.0, 5.0);

            UIData.pdSpectrum = new double[2, UIData.nConfigurationIMAQLineLength];
            ThreadData.pdSpectrum = new double[2 * UIData.nConfigurationIMAQLineLength];
            graphSpectrum.DataSource = UIData.pdSpectrum;
            axisSpectrumHorizontal.Range = new Range<double>(0, UIData.nConfigurationIMAQLineLength);
            axisSpectrumVertical.Range = new Range<double>(0, 4096);

            ThreadData.pdCalibrationPhase = new double[2, UIData.nConfigurationZPFactor * UIData.nConfigurationIMAQLineLength];
            graphCalibrationPhase.DataSource = ThreadData.pdCalibrationPhase;
            axisCalibrationPhaseHorizontal.Range = new Range<int>(0, UIData.nConfigurationZPFactor * UIData.nConfigurationIMAQLineLength);
            axisCalibrationPhaseVertical.Range = new Range<double>(0, 1000);

            ThreadData.pdCalibrationDepthProfile = new double[2, UIData.nConfigurationIMAQLineLength / 2];
            graphCalibrationDepthProfile.DataSource = ThreadData.pdCalibrationDepthProfile;
            axisCalibrationDepthProfileHorizontal.Range = new Range<int>(0, UIData.nConfigurationIMAQLineLength / 2);
            axisCalibrationDepthProfileVertical.Range = new Range<double>(0, 100);

            ThreadData.pdDispersionPhase = new double[2, UIData.nConfigurationIMAQLineLength];
            graphDispersionPhase.DataSource = ThreadData.pdDispersionPhase;
            axisDispersionPhaseHorizontal.Range = new Range<int>(0, UIData.nConfigurationIMAQLineLength);
            axisDispersionPhaseVertical.Range = new Range<double>(0, 1000);

            ThreadData.pdDispersionDepthProfile = new double[2, UIData.nConfigurationIMAQLineLength / 2];
            graphDispersionDepthProfile.DataSource = ThreadData.pdDispersionDepthProfile;
            axisDispersionDepthProfileHorizontal.Range = new Range<int>(0, UIData.nConfigurationIMAQLineLength / 2);
            axisDispersionDepthProfileVertical.Range = new Range<double>(0, 100);

            #endregion

            #region intensity graphs

            ThreadData.pdIntensity = new double[nNumberLines, nDepthProfileLength];
            UIData.pdIntensityImage = new double[nNumberLines, nDepthProfileLength];
            graphIntensity.DataSource = UIData.pdIntensityImage;

            UIData.pdIntensityTop = new double[2, nNumberLines];
            graphIntensityTop.DataSource = UIData.pdIntensityTop;
            axisIntensityTopHorizontal.Range = new Range<int>(0, nNumberLines);
            axisIntensityTopVertical.Range = new Range<double>(0, 100);

            UIData.pdIntensityLeft = new double[2, nDepthProfileLength];
            graphIntensityLeft.DataSource = UIData.pdIntensityLeft;
            axisIntensityLeftHorizontal.Range = new Range<int>(0, nDepthProfileLength);
            axisIntensityLeftVertical.Range = new Range<double>(0, 100);

            #endregion

        }

        private void btnConfigurationStop_Click(object sender, RoutedEventArgs e)
        {
            ThreadData.mrwePrimaryKernelDead.WaitOne();
            ThreadData.Destroy();
            // clear linked list
            CDataNode datanode;
            while (nodeList.Count > 0)
            {
                datanode = nodeList.Last();
                nodeList.RemoveLast();
                datanode = null;
            }   // while (nodeList.Count
        }

        private void btnConfigurationParameterFileOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                UIData.strConfigurationParameterFilename = openFileDialog.FileName;
                OpenParameterFile();
            }   // if (openFileDialog.ShowDialog
        }

        // 20210712 HY Begin //
        private void btnConfigurationParameterFileUpdate_Click(object sender, RoutedEventArgs e)
        {
            OpenParameterFile();
        }
        // 20210712 HY End  //

        void OpenParameterFile()
        {
            string line;
            StreamReader sr = new StreamReader(UIData.strConfigurationParameterFilename);
            UIData.strConfigurationParameterSummary = ""; // "\r\n";
            line = sr.ReadLine();
            while (line != null)
            {
                UIData.strConfigurationParameterSummary += line + "\r\n";
                int n1 = line.IndexOf("`");
                int n2 = line.IndexOf("'");
                if (n1 != -1)
                {
                    string parametername = line.Substring(0, n1 - 1);
                    string parametervalue = line.Substring(n1 + 1, n2 - n1 - 1);
                    if (parametername == UIData.name_strConfigurationDAQDevice) UIData.strConfigurationDAQDevice = parametervalue;
                    if (parametername == UIData.name_nConfigurationLinesPerSecond) UIData.nConfigurationLinesPerSecond = int.Parse(parametervalue);
                    if (parametername == UIData.name_nConfigurationTicksPerLine) UIData.nConfigurationTicksPerLine = int.Parse(parametervalue);
                    if (parametername == UIData.name_nConfigurationFramesInWFM) UIData.nConfigurationFramesInWFM = int.Parse(parametervalue);
                    if (parametername == UIData.name_bConfigurationIMAQDevice1) UIData.bConfigurationIMAQDevice1 = bool.Parse(parametervalue);
                    if (parametername == UIData.name_strConfigurationIMAQDevice1) UIData.strConfigurationIMAQDevice1 = parametervalue;
                    if (parametername == UIData.name_bConfigurationIMAQDevice2) UIData.bConfigurationIMAQDevice2 = bool.Parse(parametervalue);
                    if (parametername == UIData.name_strConfigurationIMAQDevice2) UIData.strConfigurationIMAQDevice2 = parametervalue;
                    if (parametername == UIData.name_nConfigurationIMAQLineLength) UIData.nConfigurationIMAQLineLength = int.Parse(parametervalue);
                    if (parametername == UIData.name_nConfigurationIMAQRingBufferLength) UIData.nConfigurationIMAQRingBufferLength = int.Parse(parametervalue);
                    if (parametername == UIData.name_bConfigurationHSDevice) UIData.bConfigurationHSDevice = bool.Parse(parametervalue);
                    if (parametername == UIData.name_strConfigurationHSDevice) UIData.strConfigurationHSDevice = parametervalue;
                    if (parametername == UIData.name_bConfigurationHSChannel1) UIData.bConfigurationHSChannel1 = bool.Parse(parametervalue);
                    if (parametername == UIData.name_bConfigurationHSChannel2) UIData.bConfigurationHSChannel2 = bool.Parse(parametervalue);
                    if (parametername == UIData.name_nConfigurationHSLineLength) UIData.nConfigurationHSLineLength = int.Parse(parametervalue);
                    if (parametername == UIData.name_nConfigurationLinesPerChunk) UIData.nConfigurationLinesPerChunk = int.Parse(parametervalue);
                    if (parametername == UIData.name_nConfigurationChunksPerImage) UIData.nConfigurationChunksPerImage = int.Parse(parametervalue);
                    if (parametername == UIData.name_nConfigurationPreIgnoreChunks) UIData.nConfigurationPreIgnoreChunks = int.Parse(parametervalue);
                    if (parametername == UIData.name_nConfigurationPostIgnoreChunks) UIData.nConfigurationPostIgnoreChunks = int.Parse(parametervalue);
                    if (parametername == UIData.name_nConfigurationImagesPerVolume) UIData.nConfigurationImagesPerVolume = int.Parse(parametervalue);
                    if (parametername == UIData.name_nConfigurationLinkedListLength) UIData.nConfigurationLinkedListLength = int.Parse(parametervalue);
                    if (parametername == UIData.name_strOperationFileDirectory) UIData.strOperationFileDirectory = parametervalue;
                    if (parametername == UIData.name_strOperationFilePrefix) UIData.strOperationFilePrefix = parametervalue;
                    if (parametername == UIData.name_nOperationFileNumber) UIData.nOperationFileNumber = int.Parse(parametervalue);
                    if (parametername == UIData.name_bOperationFileRecord) UIData.bOperationFileRecord = bool.Parse(parametervalue);
                    if (parametername == UIData.name_dOperationFastGalvoStart) UIData.dOperationFastGalvoStart = double.Parse(parametervalue);
                    if (parametername == UIData.name_dOperationFastGalvoStop) UIData.dOperationFastGalvoStop = double.Parse(parametervalue);
                    if (parametername == UIData.name_dOperationCenterX) UIData.dOperationCenterX = double.Parse(parametervalue);
                    if (parametername == UIData.name_dOperationCenterY) UIData.dOperationCenterY = double.Parse(parametervalue);
                    if (parametername == UIData.name_dOperationCenterAngle) UIData.dOperationCenterAngle = double.Parse(parametervalue);
                    if (parametername == UIData.name_dOperationScanWidth) UIData.dOperationScanWidth = double.Parse(parametervalue);
                    if (parametername == UIData.name_nOperationFastGalvoLinesPerPosition) UIData.nOperationFastGalvoLinesPerPosition = int.Parse(parametervalue);
                    if (parametername == UIData.name_dOperationSlowGalvoStart) UIData.dOperationSlowGalvoStart = double.Parse(parametervalue);
                    if (parametername == UIData.name_dOperationSlowGalvoStop) UIData.dOperationSlowGalvoStop = double.Parse(parametervalue);
                    if (parametername == UIData.name_nOperationSlowGalvoCurrentFrame) UIData.nOperationSlowGalvoCurrentFrame = int.Parse(parametervalue);
                    if (parametername == UIData.name_nConfigurationZPFactor) UIData.nConfigurationZPFactor = int.Parse(parametervalue);
                    // 20210712 HY Benginning //
                    if (parametername == UIData.name_nDataFileNumber) UIData.nDataFileNumber = int.Parse(parametervalue);
                    // 20210712 HY Benginning //
                    if (parametername == UIData.name_nReferenceType) UIData.nReferenceType = int.Parse(parametervalue);
                    if (parametername == UIData.name_nSpectrumLine) UIData.nSpectrumLine = int.Parse(parametervalue);
                    if (parametername == UIData.name_nSpectrumColorScaleMax) UIData.nSpectrumColorScaleMax = int.Parse(parametervalue);
                    if (parametername == UIData.name_nSpectrumColorScaleMin) UIData.nSpectrumColorScaleMin = int.Parse(parametervalue);
                    if (parametername == UIData.name_bCalibrate) UIData.bCalibrate = bool.Parse(parametervalue);
                    if (parametername == UIData.name_dCalibrationMax) UIData.dCalibrationMax = double.Parse(parametervalue);
                    if (parametername == UIData.name_dCalibrationMin) UIData.dCalibrationMin = double.Parse(parametervalue);
                    if (parametername == UIData.name_nCalibrationLeft) UIData.nCalibrationLeft = int.Parse(parametervalue);
                    if (parametername == UIData.name_nCalibrationRight) UIData.nCalibrationRight = int.Parse(parametervalue);
                    if (parametername == UIData.name_nCalibrationRound) UIData.nCalibrationRound = int.Parse(parametervalue);
                    if (parametername == UIData.name_dDispersionMax) UIData.dDispersionMax = double.Parse(parametervalue);
                    if (parametername == UIData.name_dDispersionMin) UIData.dDispersionMin = double.Parse(parametervalue);
                    if (parametername == UIData.name_nDispersionLeft) UIData.nDispersionLeft = int.Parse(parametervalue);
                    if (parametername == UIData.name_nDispersionRight) UIData.nDispersionRight = int.Parse(parametervalue);
                    if (parametername == UIData.name_nDispersionRound) UIData.nDispersionRound = int.Parse(parametervalue);
                    if (parametername == UIData.name_bShowIntensity) UIData.bShowIntensity = bool.Parse(parametervalue);
                    if (parametername == UIData.name_nSkipLines) UIData.nSkipLines = int.Parse(parametervalue);
                    if (parametername == UIData.name_nIntensityLine) UIData.nIntensityLine = int.Parse(parametervalue);
                    if (parametername == UIData.name_nIntensityPoint) UIData.nIntensityPoint = int.Parse(parametervalue);
                    if (parametername == UIData.name_bShowVariable) UIData.bShowVariable = bool.Parse(parametervalue);
                    if (parametername == UIData.name_nVariableLine) UIData.nVariableLine = int.Parse(parametervalue);
                    if (parametername == UIData.name_nVariablePoint) UIData.nVariablePoint = int.Parse(parametervalue);
                    if (parametername == UIData.name_nEnFaceMinDepth) UIData.nEnFaceMinDepth = int.Parse(parametervalue);
                    if (parametername == UIData.name_nEnFaceMaxDepth) UIData.nEnFaceMaxDepth = int.Parse(parametervalue);
                    if (parametername == UIData.name_nPhaseReferenceDepth) UIData.nPhaseReferenceDepth = int.Parse(parametervalue);
                }   // if (n1
                line = sr.ReadLine();
            }   // while (line
            sr.Close();
            StreamWriter lastParameter = new StreamWriter("lastparameterfilename.txt");
            lastParameter.WriteLine(UIData.strConfigurationParameterFilename);
            lastParameter.Close();
        }

        private void btnConfigurationParameterFileSave_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            if (saveFileDialog.ShowDialog() == true)
            {
                UIData.strConfigurationParameterFilename = saveFileDialog.FileName;
                StreamWriter sw = new StreamWriter(saveFileDialog.FileName);
                sw.WriteLine(UIData.name_strConfigurationDAQDevice + "=`" + UIData.strConfigurationDAQDevice + "'");
                sw.WriteLine(UIData.name_nConfigurationLinesPerSecond + "=`" + UIData.nConfigurationLinesPerSecond + "'");
                sw.WriteLine(UIData.name_nConfigurationTicksPerLine + "=`" + UIData.nConfigurationTicksPerLine + "'");
                sw.WriteLine(UIData.name_nConfigurationFramesInWFM + "=`" + UIData.nConfigurationFramesInWFM + "'");
                sw.WriteLine(UIData.name_bConfigurationIMAQDevice1 + "=`" + UIData.bConfigurationIMAQDevice1 + "'");
                sw.WriteLine(UIData.name_strConfigurationIMAQDevice1 + "=`" + UIData.strConfigurationIMAQDevice1 + "'");
                sw.WriteLine(UIData.name_bConfigurationIMAQDevice2 + "=`" + UIData.bConfigurationIMAQDevice2 + "'");
                sw.WriteLine(UIData.name_strConfigurationIMAQDevice2 + "=`" + UIData.strConfigurationIMAQDevice2 + "'");
                sw.WriteLine(UIData.name_nConfigurationIMAQLineLength + "=`" + UIData.nConfigurationIMAQLineLength + "'");
                sw.WriteLine(UIData.name_nConfigurationIMAQRingBufferLength + "=`" + UIData.nConfigurationIMAQRingBufferLength + "'");
                sw.WriteLine(UIData.name_bConfigurationHSDevice + "=`" + UIData.bConfigurationHSDevice + "'");
                sw.WriteLine(UIData.name_strConfigurationHSDevice + "=`" + UIData.strConfigurationHSDevice + "'");
                sw.WriteLine(UIData.name_bConfigurationHSChannel1 + "=`" + UIData.bConfigurationHSChannel1 + "'");
                sw.WriteLine(UIData.name_bConfigurationHSChannel2 + "=`" + UIData.bConfigurationHSChannel2 + "'");
                sw.WriteLine(UIData.name_nConfigurationHSLineLength + "=`" + UIData.nConfigurationHSLineLength + "'");
                sw.WriteLine(UIData.name_nConfigurationLinesPerChunk + "=`" + UIData.nConfigurationLinesPerChunk + "'");
                sw.WriteLine(UIData.name_nConfigurationChunksPerImage + "=`" + UIData.nConfigurationChunksPerImage + "'");
                sw.WriteLine(UIData.name_nConfigurationPreIgnoreChunks + "=`" + UIData.nConfigurationPreIgnoreChunks + "'");
                sw.WriteLine(UIData.name_nConfigurationPostIgnoreChunks + "=`" + UIData.nConfigurationPostIgnoreChunks + "'");
                sw.WriteLine(UIData.name_nConfigurationImagesPerVolume + "=`" + UIData.nConfigurationImagesPerVolume + "'");
                sw.WriteLine(UIData.name_nConfigurationLinkedListLength + "=`" + UIData.nConfigurationLinkedListLength + "'");
                sw.WriteLine(UIData.name_strOperationFileDirectory + "=`" + UIData.strOperationFileDirectory + "'");
                sw.WriteLine(UIData.name_strOperationFilePrefix + "=`" + UIData.strOperationFilePrefix + "'");
                sw.WriteLine(UIData.name_nOperationFileNumber + "=`" + UIData.nOperationFileNumber + "'");
                sw.WriteLine(UIData.name_bOperationFileRecord + "=`" + UIData.bOperationFileRecord + "'");
                sw.WriteLine(UIData.name_dOperationFastGalvoStart + "=`" + UIData.dOperationFastGalvoStart + "'");
                sw.WriteLine(UIData.name_dOperationFastGalvoStop + "=`" + UIData.dOperationFastGalvoStop + "'");
                sw.WriteLine(UIData.name_dOperationCenterX + "=`" + UIData.dOperationCenterX + "'");
                sw.WriteLine(UIData.name_dOperationCenterY + "=`" + UIData.dOperationCenterY + "'");
                sw.WriteLine(UIData.name_dOperationCenterAngle + "=`" + UIData.dOperationCenterAngle + "'");
                sw.WriteLine(UIData.name_dOperationScanWidth + "=`" + UIData.dOperationScanWidth + "'");
                sw.WriteLine(UIData.name_nOperationFastGalvoLinesPerPosition + "=`" + UIData.nOperationFastGalvoLinesPerPosition + "'");
                sw.WriteLine(UIData.name_dOperationSlowGalvoStart + "=`" + UIData.dOperationSlowGalvoStart + "'");
                sw.WriteLine(UIData.name_dOperationSlowGalvoStop + "=`" + UIData.dOperationSlowGalvoStop + "'");
                sw.WriteLine(UIData.name_nOperationSlowGalvoCurrentFrame + "=`" + UIData.nOperationSlowGalvoCurrentFrame + "'");
                sw.WriteLine(UIData.name_nConfigurationZPFactor + "=`" + UIData.nConfigurationZPFactor + "'");
                // 20210712 HY Begin //
                sw.WriteLine(UIData.name_nDataFileNumber + "=`" + UIData.nDataFileNumber + "'");
                // 20210712 HY End//
                sw.WriteLine(UIData.name_nReferenceType + "=`" + UIData.nReferenceType + "'");
                sw.WriteLine(UIData.name_nSpectrumLine + "=`" + UIData.nSpectrumLine + "'");
                sw.WriteLine(UIData.name_nSpectrumColorScaleMax + "=`" + UIData.nSpectrumColorScaleMax + "'");
                sw.WriteLine(UIData.name_nSpectrumColorScaleMin + "=`" + UIData.nSpectrumColorScaleMin + "'");
                sw.WriteLine(UIData.name_bCalibrate + "=`" + UIData.bCalibrate + "'");
                sw.WriteLine(UIData.name_dCalibrationMax + "=`" + UIData.dCalibrationMax + "'");
                sw.WriteLine(UIData.name_dCalibrationMin + "=`" + UIData.dCalibrationMin + "'");
                sw.WriteLine(UIData.name_nCalibrationLeft + "=`" + UIData.nCalibrationLeft + "'");
                sw.WriteLine(UIData.name_nCalibrationRight + "=`" + UIData.nCalibrationRight + "'");
                sw.WriteLine(UIData.name_nCalibrationRound + "=`" + UIData.nCalibrationRound + "'");
                sw.WriteLine(UIData.name_dDispersionMax + "=`" + UIData.dDispersionMax + "'");
                sw.WriteLine(UIData.name_dDispersionMin + "=`" + UIData.dDispersionMin + "'");
                sw.WriteLine(UIData.name_nDispersionLeft + "=`" + UIData.nDispersionLeft + "'");
                sw.WriteLine(UIData.name_nDispersionRight + "=`" + UIData.nDispersionRight + "'");
                sw.WriteLine(UIData.name_nDispersionRound + "=`" + UIData.nDispersionRound + "'");
                sw.WriteLine(UIData.name_bShowIntensity + "=`" + UIData.bShowIntensity + "'");
                sw.WriteLine(UIData.name_nSkipLines + "=`" + UIData.nSkipLines + "'");
                sw.WriteLine(UIData.name_nIntensityLine + "=`" + UIData.nIntensityLine + "'");
                sw.WriteLine(UIData.name_nIntensityPoint + "=`" + UIData.nIntensityPoint + "'");
                sw.WriteLine(UIData.name_bShowVariable + "=`" + UIData.bShowVariable + "'");
                sw.WriteLine(UIData.name_nVariableLine + "=`" + UIData.nVariableLine + "'");
                sw.WriteLine(UIData.name_nVariablePoint + "=`" + UIData.nVariablePoint + "'");
                sw.WriteLine(UIData.name_nEnFaceMinDepth + "=`" + UIData.nEnFaceMinDepth + "'");
                sw.WriteLine(UIData.name_nEnFaceMaxDepth + "=`" + UIData.nEnFaceMaxDepth + "'");
                sw.WriteLine(UIData.name_nPhaseReferenceDepth + "=`" + UIData.nPhaseReferenceDepth + "'");
                sw.WriteLine("END OF OCT PARAMETERS");
                sw.Close();
                StreamWriter lastParameter = new StreamWriter("lastparameterfilename.txt");
                lastParameter.WriteLine(UIData.strConfigurationParameterFilename);
                lastParameter.Close();
            }   // if (saveFileDialog.ShowDialog
        }

        private void btnOperationStart_Click(object sender, RoutedEventArgs e)
        {
            ThreadData.mrwePrimaryKernelRun.Set();
        }

        private void btnOperationStop_Click(object sender, RoutedEventArgs e)
        {
            // stop threads
            ThreadData.mrwePrimaryKernelKill.Set();
        }

        private void btnOperationFileDirectoryBrowse_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnOperationGalvoUpdate_Click(object sender, RoutedEventArgs e)
        {
            ThreadData.arweWFMUpdate.Set();
        }

        private void btnDiagnosticsNodePrev_Click(object sender, RoutedEventArgs e)
        {
            // find node
            if (nodeList.Count() == 0)
            {
                UIData.nodeDiagnostics = null;
            }
            else
            {
                UIData.nodeDiagnostics = UIData.nodeDiagnostics.Previous;
                if (UIData.nodeDiagnostics == null)
                    UIData.nodeDiagnostics = nodeList.Last;
            }
        }   // private void btnDiagnosticsNodePrev_Click

        private void btnDiagnosticsNodeNext_Click(object sender, RoutedEventArgs e)
        {
            // find node
            if (nodeList.Count() == 0)
            {
                UIData.nodeDiagnostics = null;
            }
            else
            {
                UIData.nodeDiagnostics = UIData.nodeDiagnostics.Next;
                if (UIData.nodeDiagnostics == null)
                    UIData.nodeDiagnostics = nodeList.First;
            }
        }   // private void btnDiagnosticsNodeNext_Click

        void UIUpdate(object sender, EventArgs e)
        {
            // diagnostics tab
            // thread status
            UIData.strDiagnosticsPrimaryKernelStatus = String.Format("{0} ", ThreadData.nPrimaryKernelNodeID) + ThreadData.strPrimaryKernelStatus;
            UIData.strDiagnosticsWFMThreadStatus = String.Format("{0} ", ThreadData.nWFMNodeID) + ThreadData.strWFMStatus;
            UIData.strDiagnosticsAcquisitionThreadStatus = String.Format("{0} ", ThreadData.nAcquisitionNodeID) + ThreadData.strAcquisitionStatus;
            UIData.strDiagnosticsSaveThreadStatus = String.Format("{0}({1}) ", ThreadData.nSaveNodeID, ThreadData.nSaveSemaphore) + ThreadData.strSaveStatus;
            UIData.strDiagnosticsWriteThreadStatus =  String.Format("{0}", writeList.Count()) + " in queue ";
            UIData.strDiagnosticsProcessingThreadStatus = String.Format("{0}({1}) ", ThreadData.nProcessingNodeID, ThreadData.nProcessingSemaphore) + ThreadData.strProcessingStatus;
            UIData.strDiagnosticsCleanupThreadStatus = String.Format("{0}({1}) ", ThreadData.nCleanupNodeID, ThreadData.nCleanupSemaphore) + ThreadData.strCleanupStatus;
            // node list
            if ((nodeList.Count() == 0) || (UIData.nodeDiagnostics == null))
            {
                UIData.nDiagnosticsNodeID = -1;
                UIData.bDiagnosticsNodeAcquisition = false;
                UIData.bDiagnosticsNodeSave = false;
                UIData.bDiagnosticsNodeProcessing = false;
            }   // if ((nodeList.Count()
            else
            {
                UIData.nDiagnosticsNodeID = UIData.nodeDiagnostics.Value.nID;
                UIData.bDiagnosticsNodeAcquisition = UIData.nodeDiagnostics.Value.bAcquired;
                UIData.bDiagnosticsNodeSave = UIData.nodeDiagnostics.Value.bSaved;
                UIData.bDiagnosticsNodeProcessing = UIData.nodeDiagnostics.Value.bProcessed;

                LinkedListNode<CDataNode> nodeTemp = nodeList.First;
                for (int nNode = 0; nNode < nodeList.Count(); nNode++)
                {
                    if (nodeTemp.Value.mut.WaitOne(0))
                    {
                        UIData.pnGraphDiagnosticsLinkedList[nNode, 3] = 2 * Convert.ToInt32(nodeTemp.Value.bAcquired) - 1;
                        UIData.pnGraphDiagnosticsLinkedList[nNode, 2] = 2 * Convert.ToInt32(nodeTemp.Value.bSaved) - 1;
                        UIData.pnGraphDiagnosticsLinkedList[nNode, 1] = 2 * Convert.ToInt32(nodeTemp.Value.bProcessed) - 1;
                        nodeTemp.Value.mut.ReleaseMutex();
                    }   // if (nodeTemp.Value.mut.WaitOne(0))
                    else
                    {
                        UIData.pnGraphDiagnosticsLinkedList[nNode, 3] = 0;
                        UIData.pnGraphDiagnosticsLinkedList[nNode, 2] = 0;
                        UIData.pnGraphDiagnosticsLinkedList[nNode, 1] = 0;
                    }   // if (nodeTemp.Value.mut.WaitOne(0))
                    nodeTemp = nodeTemp.Next;
                }   // for (int nNode
                graphDiagnosticsNodeList.Refresh();

                System.Buffer.BlockCopy(ThreadData.pdSpectrum, 0, UIData.pdSpectrum, 0, 2 * UIData.nConfigurationIMAQLineLength * sizeof(double));

                Array.Copy(ThreadData.pdIntensity, UIData.pdIntensityImage, UIData.nConfigurationChunksPerImage * UIData.nConfigurationLinesPerChunk * UIData.nConfigurationIMAQLineLength / 2);
                
                #region variable graphs
                if (ThreadData.arweProcessingVariableChange.WaitOne(0) == true)
                {
                    int nNumberLines = UIData.nConfigurationChunksPerImage * UIData.nConfigurationLinesPerChunk;
                    int nNumberFrames = UIData.nConfigurationImagesPerVolume;
                    int nLineLength = UIData.nConfigurationIMAQLineLength;
                    switch (UIData.nVariableType)
                    {
                        case 0: // en face
                            graphVariable.DataSource = UIData.pdVariableImage;

                            graphVariableTop.DataSource = UIData.pdVariableTop;
                            axisVariableTopHorizontal.Range = new Range<int>(0, nNumberLines);
                            axisVariableTopVertical.Range = new Range<double>(0, 100);

                            graphVariableLeft.DataSource = UIData.pdVariableLeft;
                            axisVariableLeftHorizontal.Range = new Range<int>(0, nNumberFrames);
                            axisVariableLeftVertical.Range = new Range<double>(0, 100);
                            break;
                        case 1: // phase
                            graphVariable.DataSource = UIData.pdVariableImage;

                            graphVariableTop.DataSource = UIData.pdVariableTop;
                            axisVariableTopHorizontal.Range = new Range<int>(0, nNumberLines);
                            axisVariableTopVertical.Range = new Range<double>(-4, 4);

                            graphVariableLeft.DataSource = UIData.pdVariableLeft;
                            axisVariableLeftHorizontal.Range = new Range<int>(0, nLineLength / 2);
                            axisVariableLeftVertical.Range = new Range<double>(-4, 4);
                            break;
                    }
                }
                if (ThreadData.nCurrentVariableType != -1)
                {
                    switch (ThreadData.nCurrentVariableType)
                    {
                        case 0: // en face
                            Array.Copy(ThreadData.pdVariable, UIData.pdVariableImage, UIData.nConfigurationChunksPerImage * UIData.nConfigurationLinesPerChunk * UIData.nConfigurationImagesPerVolume);
                            break;
                        case 1: // phase
                            Array.Copy(ThreadData.pdVariable, UIData.pdVariableImage, UIData.nConfigurationChunksPerImage * UIData.nConfigurationLinesPerChunk * UIData.nConfigurationIMAQLineLength / 2);
                            break;
                    }
                }
                #endregion

            }   // if ((nodeList.Count()

            graphDiagnosticsWFM.Refresh();
            graphDAQ.Refresh();
            graphSpectrum.Refresh();
            graphCalibrationDepthProfile.Refresh();
            graphCalibrationPhase.Refresh();
            graphDispersionDepthProfile.Refresh();
            graphDispersionPhase.Refresh();

            graphIntensity.Refresh();
            graphIntensityLeft.Refresh();
            graphIntensityTop.Refresh();

            graphVariable.Refresh();
            graphVariableLeft.Refresh();
            graphVariableTop.Refresh();
            
            UIData.nRingDiagnostic1 = ThreadData.nRingDiagnostic1;
            UIData.nRingDiagnostic2 = ThreadData.nRingDiagnostic2;
            UIData.nRingDiagnostic3 = ThreadData.nRingDiagnostic3;
            

        }   // void UIUpdate

        void PrimaryKernel()
        {
            #region initialize primary kernel
            ThreadData.strPrimaryKernelStatus = "initializing";
            // initialization code goes here
            ThreadData.threadAcquisition.Start();
            ThreadData.threadWFM.Start();
            ThreadData.threadSave.Start();
            ThreadData.threadWrite.Start();
            ThreadData.threadScan.Start();
            ThreadData.threadProcessing.Start();
            ThreadData.threadCleanup.Start();
            // set up wait handles for main loop
            WaitHandle[] pwePrimaryKernel = new WaitHandle[2];
            pwePrimaryKernel[0] = ThreadData.mrwePrimaryKernelKill;
            pwePrimaryKernel[1] = ThreadData.mrwePrimaryKernelRun;
            // wait for other threads to finish initializing
            ThreadData.mrweAcquisitionThreadReady.WaitOne();
            ThreadData.mrweWFMThreadReady.WaitOne();
            ThreadData.mrweSaveThreadReady.WaitOne();
            ThreadData.mrweProcessingThreadReady.WaitOne();
            ThreadData.mrweCleanupThreadReady.WaitOne();
            // signal that initialization is complete
            ThreadData.mrwePrimaryKernelReady.Set();
            ThreadData.strPrimaryKernelStatus = "initialized";
            #endregion

            // main loop
            bool bFirstLoop = true;
            while (WaitHandle.WaitAny(pwePrimaryKernel) == 1)
            {
                ThreadData.strPrimaryKernelStatus = "in loop";

                #region bFirstLoop
                if (bFirstLoop)
                {
                    // signal worker threads
                    ThreadData.mrweAcquisitionThreadRun.Set();
                    ThreadData.mrweSaveThreadRun.Set();
                    ThreadData.mrweWriteThreadRun.Set();
                    ThreadData.mrweScanThreadRun.Set();
                    ThreadData.mrweProcessingThreadRun.Set();
                    ThreadData.mrweCleanupThreadRun.Set();
                    //                    ThreadData.mrweWFMThreadRun.Set();
                    bFirstLoop = false;
                }
                #endregion

                #region every loop
                // actual work
                ThreadData.arwePrimaryTrigger.WaitOne();
                if ((ThreadData.nWFMSemaphore = ThreadData.sweWFMThreadTrigger.Release()) == ThreadData.nWFMMaxCount - 1)
                    ThreadData.mrwePrimaryKernelKill.Set();
                if ((ThreadData.nSaveSemaphore = ThreadData.sweSaveThreadTrigger.Release()) == ThreadData.nMaxSaveCount - 1)
                    ThreadData.mrwePrimaryKernelKill.Set();
                if ((ThreadData.nProcessingSemaphore = ThreadData.sweProcessingThreadTrigger.Release()) == ThreadData.nMaxSaveCount - 1)
                    ThreadData.mrwePrimaryKernelKill.Set();
                if ((ThreadData.nCleanupSemaphore = ThreadData.sweCleanupThreadTrigger.Release()) == ThreadData.nMaxSaveCount - 1)
                    ThreadData.mrwePrimaryKernelKill.Set();
                #endregion

            }

            #region kill other threads
            ThreadData.strPrimaryKernelStatus = "out of loop";
            ThreadData.mrweAcquisitionThreadKill.Set();
            ThreadData.mrweAcquisitionThreadDead.WaitOne();
            ThreadData.mrweWFMThreadKill.Set();
            ThreadData.mrweWFMThreadDead.WaitOne();
            ThreadData.mrweSaveThreadKill.Set();
            ThreadData.mrweSaveThreadDead.WaitOne();
            ThreadData.mrweWriteThreadKill.Set();
            ThreadData.mrweWriteThreadDead.WaitOne();
            ThreadData.mrweScanThreadKill.Set();
            ThreadData.mrweScanThreadDead.WaitOne();
            ThreadData.mrweProcessingThreadKill.Set();
            ThreadData.mrweProcessingThreadDead.WaitOne();
            ThreadData.mrweCleanupThreadKill.Set();
            ThreadData.mrweCleanupThreadDead.WaitOne();
            #endregion

            #region send signal that this thread is dead
            // signal that thread is dead
            ThreadData.strPrimaryKernelStatus = "dead";
            ThreadData.mrwePrimaryKernelDead.Set();
            #endregion

            #region reset linked list
            // temp
            LinkedListNode<CDataNode> nodeTemp;
            nodeTemp = nodeList.First;
            while (nodeTemp != null)
            {
                nodeTemp.Value.bAcquired = false;
                nodeTemp = nodeTemp.Next;
            }
            #endregion

        }   // void PrimaryKernel

        void AcquisitionThread()
        {
            ThreadData.strAcquisitionStatus = "initializing";

            ThreadData.nRingDiagnostic1 = 0;
            ThreadData.nRingDiagnostic2 = 0;
            ThreadData.nRingDiagnostic3 = 0;

            #region set up imaq
            // imaq set up
            ImaqWrapper imaqInterface = new ImaqWrapper();
            string strInterfaceName = "img0";
            char[] pchInterfaceName = new char[64];
            pchInterfaceName = strInterfaceName.ToCharArray();
            uint pfid = 0;
            uint psid = 0;
            int rvalp1 = ImaqWrapper.imgInterfaceOpen(pchInterfaceName, ref pfid);

            // Begin 20210701 HY
            Thread.Sleep(10);  // BM added
            while (rvalp1 != 0)
            {
                rvalp1 = ImaqWrapper.imgInterfaceOpen(pchInterfaceName, ref pfid);
                Thread.Sleep(5); //timeout does not help
            }
            // End 20210701 HY

            Int16[] pnTemp = null;
            uint nNumberRingBuffers = Convert.ToUInt32(UIData.nConfigurationIMAQRingBufferLength);
            IntPtr[] ppnImaqBuffers;
            ppnImaqBuffers = new IntPtr[nNumberRingBuffers];
            for (int nBuffer = 0; nBuffer < nNumberRingBuffers; nBuffer++)
                ppnImaqBuffers[nBuffer] = new IntPtr();
            if (rvalp1 == 0)
            {
                UInt32 nROIHeight = Convert.ToUInt32(UIData.nConfigurationLinesPerChunk);
                UInt32 nROIWidth = Convert.ToUInt32(UIData.nConfigurationIMAQLineLength);
                UInt32 nBitsPerPixel = 0;
                int rvalR02 = ImaqWrapper.imgSessionOpen(pfid, ref psid);
                int rvalR03 = ImaqWrapper.imgSetAttribute2(psid, ImaqWrapper.IMG_ATTR_ROI_HEIGHT, nROIHeight);
                int rvalR04 = ImaqWrapper.imgSetAttribute2(psid, ImaqWrapper.IMG_ATTR_ROI_WIDTH, nROIWidth);
                int rvalR05 = ImaqWrapper.imgGetAttribute(psid, ImaqWrapper.IMG_ATTR_ROI_HEIGHT, ref nROIHeight);
                int rvalR06 = ImaqWrapper.imgGetAttribute(psid, ImaqWrapper.IMG_ATTR_ROI_WIDTH, ref nROIWidth);
                int rvalR07 = ImaqWrapper.imgGetAttribute(psid, ImaqWrapper.IMG_ATTR_BITSPERPIXEL, ref nBitsPerPixel);
                int rvalR08 = ImaqWrapper.imgRingSetup(psid, nNumberRingBuffers, ppnImaqBuffers, 0, 0);
                pnTemp = new Int16[nROIHeight * nROIWidth];

                // Begin 20210701 HY
                while (rvalR02 != 0 || rvalR03 != 0 || rvalR04 != 0 || rvalR05 != 0 || rvalR06 != 0 || rvalR07 != 0 || rvalR08 != 0)
                {
                    Thread.Sleep(10); //BM added
                    rvalR02 = ImaqWrapper.imgSessionOpen(pfid, ref psid);
                    rvalR03 = ImaqWrapper.imgSetAttribute2(psid, ImaqWrapper.IMG_ATTR_ROI_HEIGHT, nROIHeight);
                    rvalR04 = ImaqWrapper.imgSetAttribute2(psid, ImaqWrapper.IMG_ATTR_ROI_WIDTH, nROIWidth);
                    rvalR05 = ImaqWrapper.imgGetAttribute(psid, ImaqWrapper.IMG_ATTR_ROI_HEIGHT, ref nROIHeight);
                    rvalR06 = ImaqWrapper.imgGetAttribute(psid, ImaqWrapper.IMG_ATTR_ROI_WIDTH, ref nROIWidth);
                    rvalR07 = ImaqWrapper.imgGetAttribute(psid, ImaqWrapper.IMG_ATTR_BITSPERPIXEL, ref nBitsPerPixel);
                    rvalR08 = ImaqWrapper.imgRingSetup(psid, nNumberRingBuffers, ppnImaqBuffers, 0, 0);
                }
                // End 20210701 HY

            }
            else
            {
                ThreadData.mrwePrimaryKernelKill.Set();
            }
            #endregion

            #region set up daq
            // DAQmx input task
            string strDAQDevice = UIData.strConfigurationDAQDevice.TrimEnd();
            string strEndofString = "/";
            if (strDAQDevice.Substring(strDAQDevice.Length - 1, 1) != strEndofString)
                strDAQDevice += strEndofString;
//            string strTrigger = strDAQDevice + "APFI0";
            strDAQDevice = "/Dev3/";
            Task taskWFMAcquisition = new Task();
            taskWFMAcquisition.AIChannels.CreateVoltageChannel(strDAQDevice + "ai0", "DAQCh1", AITerminalConfiguration.Differential, -10.0, 10.0, AIVoltageUnits.Volts);
            taskWFMAcquisition.AIChannels.CreateVoltageChannel(strDAQDevice + "ai1", "DAQCh2", AITerminalConfiguration.Differential, -10.0, 10.0, AIVoltageUnits.Volts);
            taskWFMAcquisition.AIChannels.CreateVoltageChannel(strDAQDevice + "ai2", "DAQfastgalvo", AITerminalConfiguration.Differential, -10.0, 10.0, AIVoltageUnits.Volts);
            taskWFMAcquisition.AIChannels.CreateVoltageChannel(strDAQDevice + "ai3", "DAQslowgalvo", AITerminalConfiguration.Differential, -10.0, 10.0, AIVoltageUnits.Volts);
            taskWFMAcquisition.Timing.ConfigureSampleClock(strDAQDevice + "PFI0", UIData.nConfigurationLinesPerSecond, SampleClockActiveEdge.Falling, SampleQuantityMode.ContinuousSamples, UIData.nConfigurationLinesPerChunk);
            taskWFMAcquisition.Stream.ReadAllAvailableSamples = true;
//            taskWFMAcquisition.Triggers.StartTrigger.ConfigureAnalogEdgeTrigger(strTrigger, AnalogEdgeStartTriggerSlope.Rising, 1.0);
            taskWFMAcquisition.Control(TaskAction.Verify);
            AnalogMultiChannelReader wfmReader = new AnalogMultiChannelReader(taskWFMAcquisition.Stream);
            #endregion

            // data structures
            double[,] pdWFMAcquisition;

            // temp initialization
            LinkedListNode<CDataNode> nodeTemp;
            nodeTemp = nodeList.First;

            // set up wait handles for main loop
            WaitHandle[] pweAcquisition = new WaitHandle[2];
            pweAcquisition[0] = ThreadData.mrweAcquisitionThreadKill;
            pweAcquisition[1] = ThreadData.mrweAcquisitionThreadRun;

            // signal that initialization is complete
            ThreadData.mrweAcquisitionThreadReady.Set();
            ThreadData.strAcquisitionStatus = "initialized";
            // also to outside world, ie Matlab
            string fpath = @"E:\Bas\matlabprogs\Matlab_nOCT_comm\readytogo.txt";
            FileStream fls = File.Create(fpath);

            // first loop?
            bool bFirstLoop = true;
            int nFileNumber = UIData.nOperationFileNumber;

            // 20210712 HY Beginning // 
            int nFileNumberResidue = UIData.nDataFileNumber % 100;
            int nFileCounter = UIData.nOperationFileNumber + UIData.nDataFileNumber - 1;
            // 20210712 HY End//

            int nFrameNumber = 1;
            int nChunk;
            int nLine0, nLineX;
            uint nAcquiredBuffer = 0;
            IntPtr pnBufferAddr = new IntPtr();
            uint nSeekBuffer = 0;

            /*HY 1208/2021 Edit*/
            int Counter = 0;
            UInt32 currBufNum = 0;
            UInt32 actBufNum = 0;
            UInt32 lastBufNum = 0;

            /*HY 1208/2021 Edit*/


            // main loop
            while (WaitHandle.WaitAny(pweAcquisition) == 1)
            {
                // Begin 20210902 HY
                //Stopwatch stopWatch = new Stopwatch();
                //stopWatch.Start();
                // End 20210902 HY
                ThreadData.strAcquisitionStatus = "in loop";
                // grab mutex
                nodeTemp.Value.mut.WaitOne();
                if (bFirstLoop)
                {
                    // imaq test
                    int rvalp4 = ImaqWrapper.imgSessionStartAcquisition(psid);
//                    taskWFMAcquisition.Start();
//                    wfmReader.BeginReadMultiSample(UIData.nConfigurationLinesPerChunk, null, taskWFMAcquisition);
                    bFirstLoop = false;
                    nFileNumber = UIData.nOperationFileNumber;
                    UIData.nOperationSlowGalvoCurrentFrame = nFrameNumber;

                    ThreadData.mrweWFMThreadRun.Set();
                    /*
                    for (nChunk = 0; nChunk < UIData.nConfigurationPreIgnoreChunks + UIData.nConfigurationChunksPerImage + UIData.nConfigurationPostIgnoreChunks; nChunk++)
                    {
//                        pdWFMAcquisition = wfmReader.ReadMultiSample(UIData.nConfigurationLinesPerChunk);
                        
                        ImaqWrapper.imgSessionExamineBuffer2(psid, nSeekBuffer, ref nAcquiredBuffer, ref pnBufferAddr);
                        ImaqWrapper.imgSessionReleaseBuffer(psid);
                        ThreadData.nRingDiagnostic2 = ImaqWrapper.imgSessionCopyBuffer(psid, nAcquiredBuffer % nNumberRingBuffers, pnTemp, 0);
                        nSeekBuffer = nAcquiredBuffer + 1;
                        
                    }
                    */
                }   // if (bFirstLoop
                if (nodeTemp.Value.bAcquired == true)
                {
                    ThreadData.mrwePrimaryKernelKill.Set();
                    nodeTemp.Value.mut.ReleaseMutex();
                }   // if (nodeTemp.Value.bAcquired
                else
                {
                    ThreadData.nAcquisitionNodeID = nodeTemp.Value.nID;
                    ThreadData.strAcquisitionStatus = "acquiring";
                    nodeTemp.Value.strFilename = UIData.strOperationFileDirectory + UIData.strOperationFilePrefix + String.Format("{0}", nFileNumber) + ".dat";
                    nodeTemp.Value.bRecord = UIData.bOperationFileRecord;
                    nodeTemp.Value.nFrameNumber = nFrameNumber;
                    nFileNumber++;
                    nFrameNumber++;
                    if (nFrameNumber > UIData.nConfigurationImagesPerVolume)
                        nFrameNumber = 1;
                    UIData.nOperationFileNumber = nFileNumber;
                    UIData.nOperationSlowGalvoCurrentFrame = nFrameNumber;

                    // 20210712 HY Beginning // 

                    /*
                    if (nFileNumber >= nFileCounter & (ThreadData.nAcquisitionNodeID + 1 ) >= nFileNumberResidue & nodeTemp.Value.bRecord & (ThreadData.nSaveNodeID + 2) >= nFileNumberResidue)
                    {
                        ThreadData.mrwePrimaryKernelKill.Set(); 
                    }
                    
                    if (nFileNumber >= nFileCounter & nodeTemp.Value.bRecord)
                    {
                        string pfn = UIData.strOperationFileDirectory;
                        if (Directory.GetFiles(pfn, "*", System.IO.SearchOption.TopDirectoryOnly).Length == UIData.nDataFileNumber)
                        {
                            ThreadData.mrwePrimaryKernelKill.Set(); 
                        }
                    }
                    */
                 
                    // 20210712 HY End //
 
                    // actual acquisition
                    for (nChunk = 0; nChunk < UIData.nConfigurationPreIgnoreChunks; nChunk++)
                    {
                        // pdWFMAcquisition = wfmReader.ReadMultiSample(UIData.nConfigurationLinesPerChunk);

                        /* 12/13/2021 HY edit */
                        if (Counter == 0)
                        {
                            ThreadData.nRingDiagnostic2 = ImaqWrapper.imgGetAttribute(psid, ImaqWrapper.IMG_ATTR_LAST_VALID_FRAME, ref currBufNum);
                        }
                        currBufNum++;
                        ImaqWrapper.imgSessionCopyBufferByNumber(psid, currBufNum, pnTemp, ImaqWrapper.IMG_OVERWRITE_GET_NEWEST, null, null);
                        Counter++;
                            /* 12/13/2021 HY edit */

                            /*
                            ThreadData.nRingDiagnostic1 = 11;
                            ThreadData.nRingDiagnostic2 = ImaqWrapper.imgSessionExamineBuffer2(psid, nSeekBuffer, ref nAcquiredBuffer, ref pnBufferAddr);
                            ThreadData.nRingDiagnostic1 = 12;
                            ThreadData.nRingDiagnostic2 = ImaqWrapper.imgSessionReleaseBuffer(psid);
                            ThreadData.nRingDiagnostic1 = 13;
                            ThreadData.nRingDiagnostic2 = ImaqWrapper.imgSessionCopyBuffer(psid, nAcquiredBuffer % nNumberRingBuffers, pnTemp, 0);
                            //ThreadData.nRingDiagnostic3 += Convert.ToInt32(nAcquiredBuffer - nSeekBuffer);    // HY 10252021 modified
                            nSeekBuffer = nAcquiredBuffer + 1;
                            */
                    }
                    for (nChunk = 0; nChunk < UIData.nConfigurationChunksPerImage; nChunk++)
                    {

/*                        ThreadData.nRingDiagnostic2 = ImaqWrapper.imgGetAttribute(psid, ImaqWrapper.IMG_ATTR_LAST_VALID_FRAME, ref currBufNum);
                        while (ThreadData.nRingDiagnostic2 != 0 | currBufNum == lastBufNum)
                        {
                            ThreadData.nRingDiagnostic2 = ImaqWrapper.imgGetAttribute(psid, ImaqWrapper.IMG_ATTR_LAST_VALID_FRAME, ref currBufNum);
                        }
                        currBufNum++;
                        lastBufNum = currBufNum;
                        ImaqWrapper.imgSessionCopyBufferByNumber(psid, currBufNum, nodeTemp.Value.pnIMAQ[nChunk], ImaqWrapper.IMG_OVERWRITE_GET_NEWEST, null, null);
*/
                        /* 12/13/2021 HY edit */
                        if (Counter == 0)
                        {
                            ThreadData.nRingDiagnostic2 = ImaqWrapper.imgGetAttribute(psid, ImaqWrapper.IMG_ATTR_LAST_VALID_FRAME, ref currBufNum);
                        }
                        currBufNum++; 
                        ImaqWrapper.imgSessionCopyBufferByNumber(psid, currBufNum, nodeTemp.Value.pnIMAQ[nChunk], ImaqWrapper.IMG_OVERWRITE_GET_NEWEST, null, null);
                        Counter++;
                       
                        /* 12/13/2021 HY edit */
                        
                        /*
//                        pdWFMAcquisition = wfmReader.ReadMultiSample(UIData.nConfigurationLinesPerChunk);
                        // copy data over to data structure
                        ThreadData.nRingDiagnostic1 = 21;
                        ThreadData.nRingDiagnostic2 = ImaqWrapper.imgSessionExamineBuffer2(psid, nSeekBuffer, ref nAcquiredBuffer, ref pnBufferAddr);
                        ThreadData.nRingDiagnostic1 = 22;
                        ThreadData.nRingDiagnostic2 = ImaqWrapper.imgSessionReleaseBuffer(psid);
                        ThreadData.nRingDiagnostic1 = 23;
                        ThreadData.nRingDiagnostic2 = ImaqWrapper.imgSessionCopyBuffer(psid, nAcquiredBuffer % nNumberRingBuffers, nodeTemp.Value.pnIMAQ[nChunk], 0);

                        //ThreadData.nRingDiagnostic3 += Convert.ToInt32(nAcquiredBuffer - nSeekBuffer); // HY 10252021 modified
                        nSeekBuffer = nAcquiredBuffer + 1;
                        nLine0 = nChunk * UIData.nConfigurationLinesPerChunk;
//                        for (int nAline = 0; nAline < UIData.nConfigurationLinesPerChunk; nAline++)
//                        {
//                            nLineX = nLine0 + nAline;
//                            nodeTemp.Value.pnDAQ[4 * nLineX + 0] = pdWFMAcquisition[0, nAline];
//                            nodeTemp.Value.pnDAQ[4 * nLineX + 1] = pdWFMAcquisition[1, nAline];
//                            nodeTemp.Value.pnDAQ[4 * nLineX + 2] = pdWFMAcquisition[2, nAline];
//                            nodeTemp.Value.pnDAQ[4 * nLineX + 3] = pdWFMAcquisition[3, nAline];
//                        }
                         */
                    }
                    for (nChunk = 0; nChunk < UIData.nConfigurationPostIgnoreChunks; nChunk++)
                    {
                        /* 12/13/2021 HY edit */
                       if (Counter == 0)
                       {
                           ThreadData.nRingDiagnostic2 = ImaqWrapper.imgGetAttribute(psid, ImaqWrapper.IMG_ATTR_LAST_VALID_FRAME, ref currBufNum);
                       }

                        currBufNum++;

                        ImaqWrapper.imgSessionCopyBufferByNumber(psid, currBufNum, pnTemp, ImaqWrapper.IMG_OVERWRITE_GET_NEWEST, null, null);
                        Counter++;
                        /* 12/13/2021 HY edit */

                        /*
//                        pdWFMAcquisition = wfmReader.ReadMultiSample(UIData.nConfigurationLinesPerChunk);
                        ThreadData.nRingDiagnostic1 = 31;
                        ThreadData.nRingDiagnostic2 = ImaqWrapper.imgSessionExamineBuffer2(psid, nSeekBuffer, ref nAcquiredBuffer, ref pnBufferAddr);
                        ThreadData.nRingDiagnostic1 = 32;
                        ThreadData.nRingDiagnostic2 = ImaqWrapper.imgSessionReleaseBuffer(psid);
                        ThreadData.nRingDiagnostic1 = 33;
                        ThreadData.nRingDiagnostic2 = ImaqWrapper.imgSessionCopyBuffer(psid, nAcquiredBuffer % nNumberRingBuffers, pnTemp, 0);
                        //ThreadData.nRingDiagnostic3 += Convert.ToInt32(nAcquiredBuffer - nSeekBuffer); // HY 10252021 modified
                        nSeekBuffer = nAcquiredBuffer + 1;
                        */
                    }
                    // set boolean to indicate completion of acquisition
                    nodeTemp.Value.bAcquired = true;
                    // release mutex
                    nodeTemp.Value.mut.ReleaseMutex();
                    // signal to primary kernel
                    ThreadData.arwePrimaryTrigger.Set();
                }   // if (nodeTemp.Value.bAcquired

                nodeTemp = nodeTemp.Next;
                if (nodeTemp == null)
                    nodeTemp = nodeList.First;
            }   // while (WaitHandle.WaitAny(pweAcquisition

            ThreadData.strAcquisitionStatus = "out of loop";
            //            wfmReader.EndReadMultiSample(null);
            int rvaln4 = ImaqWrapper.imgSessionStopAcquisition(psid);
            int rvaln2 = ImaqWrapper.imgClose(psid, 0);
            int rvaln1 = ImaqWrapper.imgClose(pfid, 0);
            // signal that thread is dead
//            taskWFMAcquisition.Stop();
//            taskWFMAcquisition.Dispose();
            ThreadData.mrweAcquisitionThreadDead.Set();
            ThreadData.strAcquisitionStatus = "dead";
        }   // void AcquisitionThread

        void WFMThread()
        {
            ThreadData.strWFMStatus = "initializing";
            // initialization code goes here
            int nPreIgnoreChunks = UIData.nConfigurationPreIgnoreChunks;
            int nChunksPerImage = UIData.nConfigurationChunksPerImage;
            int nPostIgnoreChunks = UIData.nConfigurationPostIgnoreChunks;
            int nLinesPerChunk = UIData.nConfigurationLinesPerChunk;
            int nFramesInWFM = UIData.nConfigurationFramesInWFM;
            int nImagesPerVolume = UIData.nConfigurationImagesPerVolume;
            int nLinesPerFrame = (nPreIgnoreChunks + nChunksPerImage + nPostIgnoreChunks) * nLinesPerChunk;
            int nTicksPerLine = UIData.nConfigurationTicksPerLine;
            int nSection = 0;
            int nFrame = 0;
            double dFastGalvoStart = UIData.dOperationFastGalvoStart;
            double dFastGalvoStop = UIData.dOperationFastGalvoStop;
            int nLinesPerPosition = UIData.nOperationFastGalvoLinesPerPosition;
            double dSlowGalvoStart = UIData.dOperationSlowGalvoStart;
            double dSlowGalvoStop = UIData.dOperationSlowGalvoStop;
            double dCenterX = UIData.dOperationCenterX * 469/1000;
            double dCenterY = UIData.dOperationCenterY * 469 / 1000;
            double dCenterAngle = UIData.dOperationCenterAngle;
            double dScanWidth = UIData.dOperationScanWidth * 469 / 1000;
            double dXStart;
            double dXStop;
            double dZStart;
            double dZStop;

            int nTicksPerFrame = (UIData.nConfigurationPreIgnoreChunks + UIData.nConfigurationChunksPerImage + UIData.nConfigurationPostIgnoreChunks) * (UIData.nConfigurationLinesPerChunk * UIData.nConfigurationTicksPerLine);
            ThreadData.pdWFMInMemory = new double[2, UIData.nConfigurationFramesInWFM * nTicksPerFrame];
            // task
            string strWFMDevice = UIData.strConfigurationDAQDevice.TrimEnd();
            string strEndofString = "/";
            if (strWFMDevice.Substring(strWFMDevice.Length - 1, 1) != strEndofString)
                strWFMDevice += strEndofString;
//            string strTrigger = strWFMDevice + "ai/StartTrigger";
//            DigitalEdgeStartTriggerEdge triggerEdge = DigitalEdgeStartTriggerEdge.Rising;

            Task taskDigitalWFMOutput = new Task();
            taskDigitalWFMOutput.DOChannels.CreateChannel(strWFMDevice + "port0/line0", "sld", ChannelLineGrouping.OneChannelForEachLine);
            DigitalSingleChannelWriter wfmDigitalWriter = new DigitalSingleChannelWriter(taskDigitalWFMOutput.Stream);
            wfmDigitalWriter.WriteSingleSampleSingleLine(true, true);

            Task taskWFMOutput = new Task();
            int nBaseInternalClockRate = UIData.nConfigurationLinesPerSecond * UIData.nConfigurationTicksPerLine;
            AnalogMultiChannelWriter wfmWriter = new AnalogMultiChannelWriter(taskWFMOutput.Stream);  
            taskWFMOutput.AOChannels.CreateVoltageChannel(strWFMDevice + "ao0", "wfm_fastgalvo", -5.0, +5.0, AOVoltageUnits.Volts);
            taskWFMOutput.AOChannels.CreateVoltageChannel(strWFMDevice + "ao1", "wfm_slowgalvo", -5.0, +5.0, AOVoltageUnits.Volts);           
            taskWFMOutput.Timing.ConfigureSampleClock(strWFMDevice + "PFI13", nBaseInternalClockRate, SampleClockActiveEdge.Rising, SampleQuantityMode.ContinuousSamples);
            //            taskWFMOutput.Triggers.StartTrigger.ConfigureDigitalEdgeTrigger(strTrigger, triggerEdge);
//            taskWFMOutput.Stream.WriteRegenerationMode = WriteRegenerationMode.DoNotAllowRegeneration;
            taskWFMOutput.Stream.WriteRegenerationMode = WriteRegenerationMode.AllowRegeneration;
            taskWFMOutput.Control(TaskAction.Verify);

             /*
            //waveform option 1: 
            dXStart = dCenterX - ((dScanWidth / 2) * Math.Cos(dCenterAngle * Math.Acos(-1)/180));
            dXStop = dCenterX + ((dScanWidth / 2) * Math.Cos(dCenterAngle * Math.Acos(-1)/180));
            dZStart = dCenterY - ((dScanWidth / 2) * Math.Sin(dCenterAngle * Math.Acos(-1)/180));
            dZStop = dCenterY + ((dScanWidth / 2) * Math.Sin(dCenterAngle * Math.Acos(-1)/180));

            for (int nTemp = 0; nTemp < nFramesInWFM; nTemp++)
            {
                for (int nLine = 0; nLine < nLinesPerFrame; nLine++)
                {
                    for (int nTick = 0; nTick < nTicksPerLine; nTick++)
                    {
                        if (((nPreIgnoreChunks * nLinesPerChunk) <= nLine) && (nLine < ((nPreIgnoreChunks + nChunksPerImage) * nLinesPerChunk)))
                        {
                           ThreadData.pdWFMInMemory[0, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dZStart + (dZStop - dZStart) * (1.0 * ((nLine - (nLine % nLinesPerPosition)) - (nPreIgnoreChunks * nLinesPerChunk))) / (1.0 * (nChunksPerImage * nLinesPerChunk));
                           ThreadData.pdWFMInMemory[1, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dXStart + (dXStop - dXStart) * (1.0 * ((nLine - (nLine % nLinesPerPosition)) - (nPreIgnoreChunks * nLinesPerChunk))) / (1.0 * (nChunksPerImage * nLinesPerChunk));
                        }
                        else
                        {
                            ThreadData.pdWFMInMemory[0, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dZStart;
                            ThreadData.pdWFMInMemory[1, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dXStart;
                        }
                        if (nLine >= ((nPreIgnoreChunks + nChunksPerImage) * nLinesPerChunk))
                        {
                            ThreadData.pdWFMInMemory[0, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dZStop;
                            ThreadData.pdWFMInMemory[1, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dXStop;
                        }
                    }
//                    for (int nTick = 0; nTick < nTicksPerLine; nTick++)
//                   {
//                        if (((nPreIgnoreChunks * nLinesPerChunk) <= nLine) && (nLine < ((nPreIgnoreChunks + nChunksPerImage) * nLinesPerChunk)))
//                            ThreadData.pdWFMInMemory[0, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dZStart + (dZStop - dZStart) * (1.0 * ((nLine - (nLine % nLinesPerPosition)) - (nPreIgnoreChunks * nLinesPerChunk))) / (1.0 * (nChunksPerImage * nLinesPerChunk));
//                        else
//                            ThreadData.pdWFMInMemory[0, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dZStart;
//                        ThreadData.pdWFMInMemory[1, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dXStart + (dXStop - dXStart) * (1.0 * ((nLine - (nLine % nLinesPerPosition)) - (nPreIgnoreChunks * nLinesPerChunk))) / (1.0 * (nChunksPerImage * nLinesPerChunk));
//                    }
                }
                nSection++;
                if (nSection >= nImagesPerVolume)
                    nSection = 0;
                nFrame++;
                if (nFrame >= nFramesInWFM)
                    nFrame = 0;
            }
            wfmWriter.WriteMultiSample(false, ThreadData.pdWFMInMemory);
            */
            
            
            // waveform option 2: 
            for (int nTemp = 0; nTemp < nFramesInWFM; nTemp++)
            {
                for (int nLine = 0; nLine < nLinesPerFrame; nLine++)
                {
                    for (int nTick = 0; nTick < nTicksPerLine; nTick++)
                    {
                        if (((nPreIgnoreChunks * nLinesPerChunk) <= nLine) && (nLine < ((nPreIgnoreChunks + nChunksPerImage) * nLinesPerChunk)))
                            ThreadData.pdWFMInMemory[0, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dFastGalvoStart + (dFastGalvoStop - dFastGalvoStart) * (1.0 * ((nLine - (nLine % nLinesPerPosition)) - (nPreIgnoreChunks * nLinesPerChunk))) / (1.0 * (nChunksPerImage * nLinesPerChunk));
                        else
                            ThreadData.pdWFMInMemory[0, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dFastGalvoStart;
                        ThreadData.pdWFMInMemory[1, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dSlowGalvoStart + (dSlowGalvoStop - dSlowGalvoStart) * ((1.0 * nSection) / (1.0 * nImagesPerVolume));
                    }
                }
                nSection++;
                if (nSection >= nImagesPerVolume)
                    nSection = 0;
                nFrame++;
                if (nFrame >= nFramesInWFM)
                    nFrame = 0;
            }
            wfmWriter.WriteMultiSample(false, ThreadData.pdWFMInMemory);
            


            // initialization code goes here

            // set up wait handles for main loop
            WaitHandle[] pweWFM = new WaitHandle[2];
            pweWFM[0] = ThreadData.mrweWFMThreadKill;
            pweWFM[1] = ThreadData.mrweWFMThreadRun;

            // secondary handle array
            WaitHandle[] pweSecondary = new WaitHandle[2];
            pweSecondary[0] = ThreadData.mrweWFMThreadKill;
            pweSecondary[1] = ThreadData.sweWFMThreadTrigger;

            // signal that initialization is complete
            ThreadData.mrweWFMThreadReady.Set();
            ThreadData.strWFMStatus = "initialized";

            // first loop
            bool bFirstLoop = true;

            // main loop
            while (WaitHandle.WaitAny(pweWFM) == 1)
            {
                ThreadData.strWFMStatus = "in loop";
                if (bFirstLoop)
                {
                    taskWFMOutput.Start();
                    bFirstLoop = false;
                }
                if (WaitHandle.WaitAny(pweSecondary) == 1)
                {
                    if (ThreadData.arweWFMUpdate.WaitOne(0) == true)
                    {
                        dFastGalvoStart = UIData.dOperationFastGalvoStart;
                        dFastGalvoStop = UIData.dOperationFastGalvoStop;
                        nLinesPerPosition = UIData.nOperationFastGalvoLinesPerPosition;
                        dSlowGalvoStart = UIData.dOperationSlowGalvoStart;
                        dSlowGalvoStop = UIData.dOperationSlowGalvoStop;
                        dCenterX = UIData.dOperationCenterX * 469 / 1000;
                        dCenterY = UIData.dOperationCenterY * 469 / 1000;
                        dCenterAngle = UIData.dOperationCenterAngle;
                        dScanWidth = UIData.dOperationScanWidth * 469 / 1000;

                        /* 
                        // waveform option 1: 
                        dXStart = dCenterX - ((dScanWidth / 2) * Math.Cos(dCenterAngle * Math.Acos(-1)/180));
                        dXStop = dCenterX + ((dScanWidth / 2) * Math.Cos(dCenterAngle * Math.Acos(-1)/180));
                        dZStart = dCenterY - ((dScanWidth / 2) * Math.Sin(dCenterAngle * Math.Acos(-1)/180));
                        dZStop = dCenterY + ((dScanWidth / 2) * Math.Sin(dCenterAngle * Math.Acos(-1)/180));
                        for (int nTemp = 0; nTemp < nFramesInWFM; nTemp++)
                        {
                            for (int nLine = 0; nLine < nLinesPerFrame; nLine++)
                            {
                                for (int nTick = 0; nTick < nTicksPerLine; nTick++)
                                {
                                    if (((nPreIgnoreChunks * nLinesPerChunk) <= nLine) && (nLine < ((nPreIgnoreChunks + nChunksPerImage) * nLinesPerChunk)))
                                    {
                                        ThreadData.pdWFMInMemory[0, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dZStart + (dZStop - dZStart) * (1.0 * ((nLine - (nLine % nLinesPerPosition)) - (nPreIgnoreChunks * nLinesPerChunk))) / (1.0 * (nChunksPerImage * nLinesPerChunk));
                                        ThreadData.pdWFMInMemory[1, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dXStart + (dXStop - dXStart) * (1.0 * ((nLine - (nLine % nLinesPerPosition)) - (nPreIgnoreChunks * nLinesPerChunk))) / (1.0 * (nChunksPerImage * nLinesPerChunk));
                                    }
                                    else
                                    {
                                        ThreadData.pdWFMInMemory[0, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dZStart;
                                        ThreadData.pdWFMInMemory[1, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dXStart;
                                    }
                                    if (nLine >= ((nPreIgnoreChunks + nChunksPerImage) * nLinesPerChunk))
                                    {
                                        ThreadData.pdWFMInMemory[0, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dZStop;
                                        ThreadData.pdWFMInMemory[1, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dXStop;
                                    }
                                }
                                //  for (int nTick = 0; nTick < nTicksPerLine; nTick++)
                                //  {
                                //      if (((nPreIgnoreChunks * nLinesPerChunk) <= nLine) && (nLine < ((nPreIgnoreChunks + nChunksPerImage) * nLinesPerChunk)))
                                //          ThreadData.pdWFMInMemory[0, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dZStart + (dZStop - dZStart) * (1.0 * ((nLine - (nLine % nLinesPerPosition)) - (nPreIgnoreChunks * nLinesPerChunk))) / (1.0 * (nChunksPerImage * nLinesPerChunk));
                                //      else
                                //          ThreadData.pdWFMInMemory[0, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dZStart;
                                //      ThreadData.pdWFMInMemory[1, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dXStart + (dXStop - dXStart) * (1.0 * ((nLine - (nLine % nLinesPerPosition)) - (nPreIgnoreChunks * nLinesPerChunk))) / (1.0 * (nChunksPerImage * nLinesPerChunk));
                                //  }
                            }
                            nSection++;
                            if (nSection >= nImagesPerVolume)
                                nSection = 0;
                            nFrame++;
                            if (nFrame >= nFramesInWFM)
                                nFrame = 0;
                        }
                        wfmWriter.WriteMultiSample(false, ThreadData.pdWFMInMemory);
                         * */

                        // waveform option 2: 
                        for (int nTemp = 0; nTemp < nFramesInWFM; nTemp++)
                        {
                            for (int nLine = 0; nLine < nLinesPerFrame; nLine++)
                            {
                                for (int nTick = 0; nTick < nTicksPerLine; nTick++)
                                {
                                    if (((nPreIgnoreChunks * nLinesPerChunk) <= nLine) && (nLine < ((nPreIgnoreChunks + nChunksPerImage) * nLinesPerChunk)))
                                        ThreadData.pdWFMInMemory[0, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dFastGalvoStart + (dFastGalvoStop - dFastGalvoStart) * (1.0 * ((nLine - (nLine % nLinesPerPosition)) - (nPreIgnoreChunks * nLinesPerChunk))) / (1.0 * (nChunksPerImage * nLinesPerChunk));
                                    else
                                        ThreadData.pdWFMInMemory[0, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dFastGalvoStart;
                                    ThreadData.pdWFMInMemory[1, (nFrame * nLinesPerFrame + nLine) * nTicksPerLine + nTick] = dSlowGalvoStart + (dSlowGalvoStop - dSlowGalvoStart) * ((1.0 * nSection) / (1.0 * nImagesPerVolume));
                                }
                            }
                            nSection++;
                            if (nSection >= nImagesPerVolume)
                                nSection = 0;
                            nFrame++;
                            if (nFrame >= nFramesInWFM)
                                nFrame = 0;
                        }
                        wfmWriter.WriteMultiSample(false, ThreadData.pdWFMInMemory);
            
                                                                    

                    }   // if if (ThreadData.arweWFMUpdate.WaitOne
                    
                    //nSection++;
                    //if (nSection >= nImagesPerVolume)
                    //    nSection = 0;
                    //nFrame++;
                    //if (nFrame >= nFramesInWFM)
                    //    nFrame = 0;

                }   // if (WaitHandle.WaitAny(pweSecondary)
            }   // while (WaitHandle.WaitAny(pweWFM)

            ThreadData.strWFMStatus = "out of loop";
            // signal that thread is dead
            taskWFMOutput.Stop();
            taskWFMOutput.Dispose();
            ThreadData.mrweWFMThreadDead.Set();
            ThreadData.strWFMStatus = "dead";

        }   // void WFMThread

        void SaveThread()
        {
            ThreadData.strSaveStatus = "initializing";
            // initialization code goes here
            Thread.Sleep(1);

            // node to save to
            LinkedListNode<CDataNode> nodeSave;
            nodeSave = nodeList.First;

            // set up wait handles for main loop
            WaitHandle[] pweSave = new WaitHandle[2];
            pweSave[0] = ThreadData.mrweSaveThreadKill;
            pweSave[1] = ThreadData.mrweSaveThreadRun;

            // secondary handle array
            WaitHandle[] pweSecondary = new WaitHandle[2];
            pweSecondary[0] = ThreadData.sweSaveThreadTrigger;
            pweSecondary[1] = ThreadData.mrweSaveThreadKill;

            // signal that initialization is complete
            ThreadData.mrweSaveThreadReady.Set();
            ThreadData.strSaveStatus = "initialized";

            // main loop
            if (WaitHandle.WaitAny(pweSave) == 1)
            {
                ThreadData.strSaveStatus = "in loop";

                while (WaitHandle.WaitAny(pweSecondary) == 0)
                {
                    ThreadData.strSaveStatus = "semaphore triggered";
                    // grab mutex
                    nodeSave.Value.mut.WaitOne();
                    if (nodeSave.Value.bAcquired == true)
                    {
                        ThreadData.nSaveNodeID = nodeSave.Value.nID;
                        ThreadData.strSaveStatus = "saving";
                        if (nodeSave.Value.bRecord)
                        {
                            CDataNode nodeTemp = new CDataNode(nodeSave.Value);
                            writeList.AddLast(nodeTemp);
                        }
                        else
                        {
                            Thread.Sleep(1);
                        }
                        nodeSave.Value.bSaved = true;
                    }
                    else
                    {
                        ThreadData.mrwePrimaryKernelKill.Set();
                    }
                    nodeSave.Value.mut.ReleaseMutex();

                    // go to next node
                    nodeSave = nodeSave.Next;
                    if (nodeSave == null)
                        nodeSave = nodeList.First;

                }   // while (WaitHandle.WaitAny(pweSecondary)
            }   // if (WaitHandle.WaitAny(pweSave)

            ThreadData.strSaveStatus = "out of loop";

            // signal that thread is dead
            ThreadData.mrweSaveThreadDead.Set();

            ThreadData.strSaveStatus = "dead";
        }

        void WriteThread()
        {
            // initialization for writing to disk

            Thread.Sleep(1);
            string strTest;
            int nOffset1 = 4096;
            int nNumberChunks = UIData.nConfigurationChunksPerImage;
            int nLineLength = UIData.nConfigurationIMAQLineLength;
            int nLinesPerChunk = UIData.nConfigurationLinesPerChunk;
            Int16[] pnFullImage = new Int16[nLineLength * nLinesPerChunk * nNumberChunks];
            int nNumberLines = nLinesPerChunk * nNumberChunks;
            int nNumberPoints = nLineLength;
            int nOffset2 = nOffset1 + nNumberChunks * nLinesPerChunk * nLineLength * sizeof(Int16);
            Int16[] pnTemp = new Int16[UIData.nConfigurationIMAQLineLength * UIData.nConfigurationLinesPerChunk];

            int nFileCounter = UIData.nOperationFileNumber + UIData.nDataFileNumber - 1;

            // set up wait handles for main loop
            WaitHandle[] pweWrite = new WaitHandle[2];
            pweWrite[0] = ThreadData.mrweWriteThreadKill;
            pweWrite[1] = ThreadData.mrweWriteThreadRun;

            // main loop
            while (WaitHandle.WaitAny(pweWrite) == 1)
            {
                if (writeList.Count() > 0)
                {
                    LinkedListNode<CDataNode> nodeSave;
                    nodeSave = writeList.First;

                    FileStream fs = File.Open(nodeSave.Value.strFilename, FileMode.Create);
                    BinaryWriter binWriter = new BinaryWriter(fs);

                    strTest = nodeSave.Value.strFilename; binWriter.Write(strTest.Length); binWriter.Write(strTest);
                    strTest = "nFrameNumber=" + nodeSave.Value.nFrameNumber + ";"; binWriter.Write(strTest.Length); binWriter.Write(strTest);
                    strTest = "nNumberDataArrays=" + 2 + ";"; binWriter.Write(strTest.Length); binWriter.Write(strTest);

                    // array 1: IMAQ data
                    strTest = "strVar='pdIMAQ';"; binWriter.Write(strTest.Length); binWriter.Write(strTest);
                    strTest = "nOffset=" + nOffset1 + ";"; binWriter.Write(strTest.Length); binWriter.Write(strTest);
                    strTest = "nNumberLines=" + nNumberLines + ";"; binWriter.Write(strTest.Length); binWriter.Write(strTest);
                    strTest = "nNumberPoints=" + nNumberPoints + ";"; binWriter.Write(strTest.Length); binWriter.Write(strTest);
                    strTest = "strDataType='int16';"; binWriter.Write(strTest.Length); binWriter.Write(strTest);

                    // array 1: IMAQ data
                    strTest = "strVar='pdDAQ';"; binWriter.Write(strTest.Length); binWriter.Write(strTest);
                    strTest = "nOffset=" + nOffset2 + ";"; binWriter.Write(strTest.Length); binWriter.Write(strTest);
                    strTest = "nNumberLines=" + 4 + ";"; binWriter.Write(strTest.Length); binWriter.Write(strTest);
                    strTest = "nNumberPoints=" + nNumberLines + ";"; binWriter.Write(strTest.Length); binWriter.Write(strTest);
                    strTest = "strDataType='double';"; binWriter.Write(strTest.Length); binWriter.Write(strTest);

                    strTest = "END"; binWriter.Write(strTest.Length); binWriter.Write(strTest);

                    // array 1: IMAQ data
                    fs.Seek(nOffset1, SeekOrigin.Begin);
                    for (int nChunk = 0; nChunk < nNumberChunks; nChunk++)
                    {
                        // copy IMAQ data
                        Array.Copy(nodeSave.Value.pnIMAQ[nChunk], 0, pnFullImage, nChunk * UIData.nConfigurationLinesPerChunk * UIData.nConfigurationIMAQLineLength, UIData.nConfigurationLinesPerChunk * UIData.nConfigurationIMAQLineLength);
                    }
                    fs.Close();
                    var byteBufferFullImage = new byte[nNumberChunks * nLinesPerChunk * nLineLength * sizeof(Int16)];
                    Buffer.BlockCopy(pnFullImage, 0, byteBufferFullImage, 0, byteBufferFullImage.Length);
                    FileSystem.WriteAllBytes(nodeSave.Value.strFilename, byteBufferFullImage, true);
                    //for (int nLine = 0; nLine < nNumberChunks * nLinesPerChunk; nLine++)
                    //    for (int nPoint = 0; nPoint < nLineLength; nPoint++)
                    //        binWriter.Write(pnFullImage[nLine * nLineLength + nPoint]);

                    // array 2 : DAQ data
                    var byteBufferDAQ = new byte[4 * nNumberChunks * nLinesPerChunk * sizeof(Double)];
                    Buffer.BlockCopy(nodeSave.Value.pnDAQ, 0, byteBufferDAQ, 0, byteBufferDAQ.Length);
                    FileSystem.WriteAllBytes(nodeSave.Value.strFilename, byteBufferDAQ, true);
                    //fs.Seek(nOffset2, SeekOrigin.Begin);
                    //for (int nLine = 0; nLine < 4; nLine++)
                    //    for (int nPoint = 0; nPoint < nNumberChunks * nLinesPerChunk; nPoint++)
                    //        binWriter.Write(nodeSave.Value.pnDAQ[nLine * nNumberChunks * nLinesPerChunk + nPoint]);

                    
                    writeList.RemoveFirst();
          
                }

                /* 20210728 HY Benginning */
                if (UIData.bOperationFileRecord)
                {
                    string pfn = UIData.strOperationFileDirectory;
                    int fCount = Directory.GetFiles(pfn, "*", System.IO.SearchOption.TopDirectoryOnly).Length;
                    if (fCount-1 >= UIData.nDataFileNumber)
                    {
                        ThreadData.mrwePrimaryKernelKill.Set();                       
                    }
                }

                /* 20210728 HY Ends */
            }

            ThreadData.strWriteStatus = "out of loop";

            // signal that thread is dead
            ThreadData.mrweWriteThreadDead.Set();

            ThreadData.strWriteStatus = "dead";

        }

        void ScanThread()
        {
            // initialization for phase scanning

            Thread.Sleep(1);
            
            // set up wait handles for main loop
            WaitHandle[] pweScan = new WaitHandle[2];
            pweScan[0] = ThreadData.mrweScanThreadKill;
            pweScan[1] = ThreadData.mrweScanThreadRun;

            UIData.nPhaseScanFastGalvoSteps = 2;
            UIData.nPhaseScanSlowGalvoSteps = 2;

            // main loop
            while (WaitHandle.WaitAny(pweScan) == 1)
            {
                Thread.Sleep(10);
                if (UIData.bOperationPhaseScanning)
                {
                    double dFastGalvoStart = UIData.dPhaseScanFastGalvoStart;
                    double dFastGalvoStop = UIData.dPhaseScanFastGalvoStop;
                    int nFastGalvoSteps = UIData.nPhaseScanFastGalvoSteps;

                    double dSlowGalvoStart = UIData.dPhaseScanSlowGalvoStart;
                    double dSlowGalvoStop = UIData.dPhaseScanSlowGalvoStop;
                    int nSlowGalvoSteps = UIData.nPhaseScanSlowGalvoSteps;

                    int nImagesPerSpot = UIData.nPhaseScanImagesPerSpot;
                    int nRestingTime = UIData.nPhaseScanRestingTime;

                    if ((dFastGalvoStart < -2) || (dFastGalvoStart > 2))
                        dFastGalvoStart = 0;
                    if ((dFastGalvoStop < -2) || (dFastGalvoStop > 2))
                        dFastGalvoStop = 0;
                    if ((dSlowGalvoStart < -2) || (dSlowGalvoStart > 2))
                        dSlowGalvoStart = 0;
                    if ((dSlowGalvoStop < -2) || (dSlowGalvoStop > 2))
                        dSlowGalvoStop = 0;
                    if (nFastGalvoSteps < 2)
                        nFastGalvoSteps = 2;
                    if (nSlowGalvoSteps < 2)
                        nSlowGalvoSteps = 2;

                    int nSpotIndex = 0;

                    for (double dFastGalvoVoltage = dFastGalvoStart; dFastGalvoVoltage <= dFastGalvoStop; dFastGalvoVoltage += (dFastGalvoStop - dFastGalvoStart)/(nFastGalvoSteps-1))
                        for (double dSlowGalvoVoltage = dSlowGalvoStart; dSlowGalvoVoltage <= dSlowGalvoStop; dSlowGalvoVoltage += (dSlowGalvoStop - dSlowGalvoStart)/(nSlowGalvoSteps-1))
                        {
                            nSpotIndex++;

                            // move galvo to new position
                            UIData.dOperationFastGalvoStart = dFastGalvoVoltage;
                            UIData.dOperationFastGalvoStop = dFastGalvoVoltage;
                            UIData.dOperationSlowGalvoStart = dSlowGalvoVoltage;
                            UIData.dOperationSlowGalvoStop = dSlowGalvoVoltage;
                            ThreadData.arweWFMUpdate.Set();

                            Thread.Sleep(100);

                            // prepare for recording
                            UIData.strOperationFilePrefix = UIData.strPhaseScanFilePrefix + "_" + nSpotIndex.ToString() + "_";

                            // recording
                            int nOperationFileNumber = UIData.nOperationFileNumber;
                            while (UIData.nOperationFileNumber < nOperationFileNumber + UIData.nPhaseScanImagesPerSpot)
                            {
                                UIData.bOperationFileRecord = true;
                            }
                            UIData.bOperationFileRecord = false;
                            while (writeList.Count()>0)
                            {
                                Thread.Sleep(100);
                            }
                            Thread.Sleep(UIData.nPhaseScanRestingTime);
                        }
                }
            }

            ThreadData.strScanStatus = "out of loop";

            // signal that thread is dead
            ThreadData.mrweScanThreadDead.Set();

            ThreadData.strScanStatus = "dead";

        }

        void ProcessingThread()
        {
            #region initialization

            ThreadData.strProcessingStatus = "initializing";

            #region variables for thread and data node structure
            // node to process
            LinkedListNode<CDataNode> nodeProcessing;
            nodeProcessing = nodeList.First;
            // set up wait handles for main loop
            WaitHandle[] pweProcessing = new WaitHandle[2];
            pweProcessing[0] = ThreadData.mrweProcessingThreadKill;
            pweProcessing[1] = ThreadData.mrweProcessingThreadRun;
            // secondary handle array
            WaitHandle[] pweSecondary = new WaitHandle[2];
            pweSecondary[0] = ThreadData.sweProcessingThreadTrigger;
            pweSecondary[1] = ThreadData.mrweProcessingThreadKill;
            #endregion

            #region structures for quick copy from node
            // data structures
            double[] pdLocalDAQ = new double[4 * UIData.nConfigurationChunksPerImage * UIData.nConfigurationLinesPerChunk];
            Int16[] pnTemp = new Int16[UIData.nConfigurationIMAQLineLength * UIData.nConfigurationLinesPerChunk];
            Int16[] pnFullImage = new Int16[UIData.nConfigurationIMAQLineLength * UIData.nConfigurationLinesPerChunk * UIData.nConfigurationChunksPerImage];
            Int16[] pnLine = new Int16[UIData.nConfigurationIMAQLineLength];                // added pnLine for data transferring to pdSpectrum
            #endregion

            #region basic definitions
            int nLineLength = UIData.nConfigurationIMAQLineLength;
            int nZPFactor = UIData.nConfigurationZPFactor;
            int nNumberLines = UIData.nConfigurationChunksPerImage * UIData.nConfigurationLinesPerChunk;
            #endregion

            #region calibration and dispersion compensation arrays
            double[] pdCalibrationZPPoint;
            pdCalibrationZPPoint = new double[nLineLength * nZPFactor];
            for (int nPoint = 0; nPoint < nLineLength * nZPFactor; nPoint++)
                pdCalibrationZPPoint[nPoint] = Convert.ToDouble(nPoint);

            int[] pnCalibrationIndex;
            pnCalibrationIndex = new int[nLineLength];
            for (int nPoint = 0; nPoint < nLineLength; nPoint++)
                pnCalibrationIndex[nPoint] = nPoint;

            ComplexDouble[] pcdDispersionCorrection;
            double[] pcdDispersionRepMatReal = new double[nNumberLines * nLineLength];     // pdDispersion repeated for dispersion compensation
            double[] pcdDispersionRepMatImag = new double[nNumberLines * nLineLength];
            pcdDispersionCorrection = new ComplexDouble[nLineLength];
            Array.Clear(pcdDispersionCorrection, 0, nLineLength);
            for (int nPoint = 0; nPoint < nLineLength; nPoint++)
            {
                pcdDispersionCorrection[nPoint].Real = 1.0;
                for (int nLine = 0; nLine < nNumberLines; nLine++)
                {
                    pcdDispersionRepMatReal[nLine * nLineLength + nPoint] = pcdDispersionCorrection[nPoint].Real;
                    pcdDispersionRepMatImag[nLine * nLineLength + nPoint] = pcdDispersionCorrection[nPoint].Imaginary;
                }
            }

            int nZPLineLength = nLineLength * nZPFactor;

            Int32[] nLineLengthArray;
            Int32[] nNumberLinesArray;
            Int32[] pnCalibrationIndex32;
            Double[] pcdDispersionCorrectionR;
            Double[] pcdDispersionCorrectionI;
            byte[] byteBufferCalibrationIndex;
            byte[] byteBufferCalibrationZPPoint;
            byte[] byteBufferLineLength;
            byte[] byteBufferReference;
            byte[] byteBufferNumberLines;
            byte[] byteArray;
            byte[] byteDispersionCorrectionR;
            byte[] byteDispersionCorrectionI;

            nLineLengthArray = new int[1];
            nNumberLinesArray = new int[1];
            pnCalibrationIndex32 = new Int32[nLineLength];
            pcdDispersionCorrectionR = new Double[nLineLength];
            pcdDispersionCorrectionI = new Double[nLineLength];
            byteBufferCalibrationIndex = new byte[nLineLength * sizeof(Int32)];
            byteBufferCalibrationZPPoint = new byte[nZPLineLength * sizeof(Double)];
            byteBufferLineLength = new byte[1 * sizeof(Int32)];
            byteBufferReference = new byte[nLineLength * sizeof(Double)];
            byteBufferNumberLines = new byte[1 * sizeof(Int32)];
            byteArray = new byte[nNumberLines * nLineLength * sizeof(Double)];
            byteDispersionCorrectionR = new byte[nLineLength * sizeof(Double)];
            byteDispersionCorrectionI = new byte[nLineLength * sizeof(Double)];





            #endregion

            #region structure to actually do processing
            int nAline;
            //int nZPLineLength = nLineLength * nZPFactor;
            double dNumberLines = Convert.ToDouble(nNumberLines);
            double[] pdArray = new double[nNumberLines * nLineLength];
            int[] pnArray = new int[nNumberLines * nLineLength];
            double[] pdReference = new double[nLineLength];
            double[] pdReferenceRepMat = new double[nNumberLines * nLineLength];    // pdReference repeated for pdArray substraction
            double[] pdAcquiredReference = new double[nLineLength];
            double[] pdLine = new double[nLineLength];
            double[] pdSum = new double[nLineLength];
            double[] pdZPSum = new double[nZPLineLength];
            ComplexDouble[] pcdLine = new ComplexDouble[nLineLength];
            ComplexDouble[] pcdFFT = new ComplexDouble[nLineLength];
            ComplexDouble[] pcdZPFFT = new ComplexDouble[nZPLineLength];
            double[] pcdZPLine = new double[nZPLineLength];                          // changed from ComplexDouble to double to receive the GPU results
            ComplexDouble[] pcdCalibrated = new ComplexDouble[nNumberLines * nLineLength];
            Array.Clear(pdZPSum, 0, nZPLineLength);
            int nSkipLines;
            double dLineCount;
            int nIntensityLine;
            int nIntensityPoint;
            int nVariableLine;
            int nVariablePoint;
            int nNumberFrames = UIData.nConfigurationImagesPerVolume;
            bool[] nFramesAcquired = new bool[nNumberFrames+1];
            int cFrames = 0;
            int nFrame;
            ThreadData.nCurrentVariableType = -1;
            double[] pdPhaseImage = new double[nNumberLines * nLineLength / 2];
            int nEnFaceMinDepth = -1;
            int nEnFaceMaxDepth = 0;
            int nPhaseReferenceDepth = 0;
            double dPhase, dReference;
            double[] pdVariablePhase = new double[nNumberLines];
            #endregion

            // signal that initialization is complete
            ThreadData.mrweProcessingThreadReady.Set();
            ThreadData.strProcessingStatus = "initialized";

            #endregion
            
            // main loop
            if (WaitHandle.WaitAny(pweProcessing) == 1)
            {
                ThreadData.strProcessingStatus = "in main loop";

                // wait for semaphore to be triggered
                while (WaitHandle.WaitAny(pweSecondary) == 0)
                {
                    ThreadData.strProcessingStatus = "semaphore triggered";

                    #region look for most recent node (look until semaphore value equals zero
                    // look for most recent node (look until semaphore value equals zero)
                    do
                    {
                        ThreadData.strProcessingStatus = "checking";
                        ThreadData.nProcessingNodeID = nodeProcessing.Value.nID;
                        nodeProcessing.Value.mut.WaitOne();
                        nodeProcessing.Value.bProcessed = true;
                        nodeProcessing.Value.mut.ReleaseMutex();
                        // move to next node
                        nodeProcessing = nodeProcessing.Next;
                        if (nodeProcessing == null)
                            nodeProcessing = nodeList.First;
                    }
                    while (ThreadData.sweProcessingThreadTrigger.WaitOne(0) == true);
                    // move back one node
                    nodeProcessing = nodeProcessing.Previous;
                    if (nodeProcessing == null)
                        nodeProcessing = nodeList.Last;
                    #endregion

                    #region copy data from node to local arrays (output: pdLocalDAQ pnFullImage)
                    // copy data
                    ThreadData.strProcessingStatus = "copying";
                    ThreadData.nProcessingNodeID = nodeProcessing.Value.nID;
                    nodeProcessing.Value.mut.WaitOne();
                    nFrame = nodeProcessing.Value.nFrameNumber;

                     // only if not in c-scan or havn't acquired this frame for the c-scan yet
                    if (UIData.bShowVariable && ThreadData.nCurrentVariableType == 0 && nFramesAcquired[nFrame])
                    {
                        nodeProcessing.Value.mut.ReleaseMutex();
                        Thread.Sleep(1);
                    }
                    else
                    {

                        var stopwatch_all = new Stopwatch();
                        stopwatch_all.Start();
                        
                        // process as usual including copy
                        Array.Copy(nodeProcessing.Value.pnDAQ, pdLocalDAQ, 4 * UIData.nConfigurationChunksPerImage * UIData.nConfigurationLinesPerChunk);
                        for (int nChunk = 0; nChunk < UIData.nConfigurationChunksPerImage; nChunk++)
                        {
                            Array.Copy(nodeProcessing.Value.pnIMAQ[nChunk], 0, pnFullImage, nChunk * UIData.nConfigurationLinesPerChunk * UIData.nConfigurationIMAQLineLength, UIData.nConfigurationLinesPerChunk * UIData.nConfigurationIMAQLineLength);
                        }
                        nodeProcessing.Value.mut.ReleaseMutex();
                        #endregion

                        ThreadData.strProcessingStatus = "processing";
                        
                        #region copy selected line to spectrum for viewing
                        // now working with just local arrays
                        nAline = UIData.nSpectrumLineNumber;
                        if (nAline != -1)
                        {
                            if (nAline < 0)
                                nAline = 0;
                            if (nAline >= UIData.nConfigurationChunksPerImage * UIData.nConfigurationLinesPerChunk)
                                nAline = UIData.nConfigurationChunksPerImage * UIData.nConfigurationLinesPerChunk - 1;
                            Array.Copy(pnFullImage, nAline * nLineLength, pnLine, 0, nLineLength);
                            pdLine = pnLine.Select(Convert.ToDouble).ToArray();
                            Array.Copy(pdLine, 0, ThreadData.pdSpectrum, 0, nLineLength);
                        }
                        else
                            Array.Clear(ThreadData.pdSpectrum, 0, 2 * nLineLength);
                        #endregion

                        nSkipLines = UIData.nSkipLines;
                        if (nSkipLines < 1)
                            nSkipLines = 1;
                        if (nSkipLines > 64)
                            nSkipLines = 64;

                        // process
                        if (UIData.bShowIntensity)
                        {
                           
                            #region reference
                            
                            // calculate reference based on reference type
                            switch (UIData.nReferenceType)
                            {
                                case 0:
                                    // no subtraction
                                    Array.Clear(pdReference, 0, nLineLength);
                                    break;
                                case 1:
                                    // subtract average
                                    pdArray = pnFullImage.Select(Convert.ToDouble).ToArray();
                                    Array.Clear(pdSum, 0, nLineLength);
                                    dLineCount = 0.0;
                                    for (int nLine = 0; nLine < nNumberLines; nLine += nSkipLines)
                                    {
                                        System.Buffer.BlockCopy(pdArray, nLine * nLineLength * sizeof(double), pdLine, 0, nLineLength * sizeof(double));
                                        pdSum = (pdSum.Zip(pdLine, (x, y) => x + y)).ToArray();
                                        dLineCount += 1.0;
                                    }
                                    for (int nPoint = 0; nPoint < nLineLength; nPoint++)
                                        pdReference[nPoint] = pdSum[nPoint] / dLineCount;
                                    break;
                                case 2:
                                    // record reference
                                    pdArray = pnFullImage.Select(Convert.ToDouble).ToArray();
                                    Array.Clear(pdSum, 0, nLineLength);
                                    dLineCount = 0.0;
                                    for (int nLine = 0; nLine < nNumberLines; nLine += nSkipLines)
                                    {
                                        System.Buffer.BlockCopy(pdArray, nLine * nLineLength * sizeof(double), pdLine, 0, nLineLength * sizeof(double));
                                        pdSum = (pdSum.Zip(pdLine, (x, y) => x + y)).ToArray();
                                        dLineCount += 1.0;
                                    }
                                    for (int nPoint = 0; nPoint < nLineLength; nPoint++)
                                        pdReference[nPoint] = pdSum[nPoint] / dLineCount;
                                    System.Buffer.BlockCopy(pdReference, 0, pdAcquiredReference, 0, nLineLength * sizeof(double));
                                    break;
                                case 3:
                                    // use recorded ref
                                    System.Buffer.BlockCopy(pdAcquiredReference, 0, pdReference, 0, nLineLength * sizeof(double));
                                    break;
                                default:
                                    break;
                            }   // switch (UIData.nReferenceType
                            // copy reference for display
                            
                            // repeat pdReference for substracting the entire pdArray
                            for (int nLine = 0; nLine < nNumberLines; nLine++)
                                System.Buffer.BlockCopy(pdReference, 0, pdReferenceRepMat, nLine*nLineLength*sizeof(double), nLineLength*sizeof(double));

                            System.Buffer.BlockCopy(pdReference, 0, ThreadData.pdSpectrum, nLineLength * sizeof(double), nLineLength * sizeof(double));
                            
                            #endregion

                            #region calibration calculation section (output: pdCalibrationZPPoint, pnCalibrationIndex)
                            
                            if (UIData.bCalibrate)
                            {
                                pdArray = pnFullImage.Select(Convert.ToDouble).ToArray();
                                // create mask for depth profiles
                                ComplexDouble[] pcdMask = new ComplexDouble[nLineLength];
                                Array.Clear(pcdMask, 0, nLineLength);
                                for (int nPoint = 0; nPoint < UIData.nCalibrationLeft - UIData.nCalibrationRound; nPoint++)
                                    pcdMask[nPoint].Real = 0.0;
                                for (int nPoint = UIData.nCalibrationLeft - UIData.nCalibrationRound; nPoint < UIData.nCalibrationLeft; nPoint++)
                                    pcdMask[nPoint].Real = 0.5 * (1.0 + Math.Cos(Math.PI * Convert.ToDouble(UIData.nCalibrationLeft - nPoint) / Convert.ToDouble(UIData.nCalibrationRound)));
                                for (int nPoint = UIData.nCalibrationLeft; nPoint < UIData.nCalibrationRight; nPoint++)
                                    pcdMask[nPoint].Real = 1.0;
                                for (int nPoint = UIData.nCalibrationRight; nPoint < UIData.nCalibrationRight + UIData.nCalibrationRound; nPoint++)
                                    pcdMask[nPoint].Real = 0.5 * (1.0 + Math.Cos(Math.PI * Convert.ToDouble(nPoint - UIData.nCalibrationRight) / Convert.ToDouble(UIData.nCalibrationRound)));
                                for (int nPoint = UIData.nCalibrationRight + UIData.nCalibrationRound; nPoint < nLineLength; nPoint++)
                                    pcdMask[nPoint].Real = 0.0;

                                // initialize calibration calculation arrays
                                ComplexDouble[] pcdMasked = new ComplexDouble[nZPLineLength];
                                ComplexDouble[] pcdSpectrum = new ComplexDouble[nZPLineLength];
                                double[] pdPhase = new double[nZPLineLength];
                                Array.Clear(pdSum, 0, nLineLength);
                                pdZPSum = new double[nZPLineLength];
                                Array.Clear(pdZPSum, 0, nZPLineLength);

                                dLineCount = 0.0;
                                for (int nLine = 0; nLine < nNumberLines; nLine += nSkipLines)
                                {
                                    System.Buffer.BlockCopy(pdArray, nLine * nLineLength * sizeof(double), pdLine, 0, nLineLength * sizeof(double));    // copy one line from array
                                    pdLine = (pdLine.Zip(pdReference, (x, y) => x - y)).ToArray();                                                      // subtract reference
                                    pcdFFT = NationalInstruments.Analysis.Dsp.Transforms.RealFft(pdLine);                                               // fft
                                    pdSum = (pdSum.Zip(pcdFFT, (x, y) => x + (y.Magnitude) * (y.Magnitude))).ToArray();                                 // keep running sum of depth profile shape
                                    Array.Clear(pcdMasked, 0, nZPLineLength);                                                                           // cut out peak
                                    for (int nPoint = 0; nPoint < nLineLength; nPoint++)
                                        pcdMasked[nPoint] = pcdFFT[nPoint].Multiply(pcdMask[nPoint]);
                                    pcdSpectrum = NationalInstruments.Analysis.Dsp.Transforms.InverseFft(pcdMasked, false);                             // inverse fft
                                    for (int nPoint = 0; nPoint < nZPLineLength; nPoint++)                                                              // calculate phase
                                        pdPhase[nPoint] = pcdSpectrum[nPoint].Phase;
                                    NationalInstruments.Analysis.Dsp.SignalProcessing.UnwrapPhase(pdPhase);                                             // unwrap phase
                                    pdZPSum = (pdZPSum.Zip(pdPhase, (x, y) => x + y)).ToArray();                                                        // keep running sum of phases
                                    dLineCount += 1.0;
                                }   // for (int nLine

                                double dMin = pdZPSum[0] / dLineCount;
                                double dMax = pdZPSum[nZPLineLength - 1] / dLineCount;
                                //                        axisCalibrationPhaseVertical.Range = new Range<double>(dMin, dMax);
                                double dSlope = (dMax - dMin) / Convert.ToDouble(nZPLineLength);
                                for (int nPoint = 0; nPoint < nZPLineLength; nPoint++)
                                {
                                    ThreadData.pdCalibrationPhase[0, nPoint] = pdZPSum[nPoint] / dLineCount;
                                    ThreadData.pdCalibrationPhase[1, nPoint] = dMin + dSlope * Convert.ToDouble(nPoint);
                                    pdCalibrationZPPoint[nPoint] = Convert.ToDouble(nPoint) + (ThreadData.pdCalibrationPhase[0, nPoint] - ThreadData.pdCalibrationPhase[1, nPoint]) / dSlope;
                                }   // for (int nPoint

                                // update calibration depth profile graph
                                for (int nPoint = 0; nPoint < nLineLength / 2; nPoint++)
                                {
                                    ThreadData.pdCalibrationDepthProfile[0, nPoint] = 10.0 * Math.Log10(pdSum[nPoint] / dLineCount);
                                    ThreadData.pdCalibrationDepthProfile[1, nPoint] = 10.0 * Math.Log10((pcdMask[nPoint].Magnitude) * (pcdMask[nPoint].Magnitude) * (pdSum[nPoint] / dLineCount));
                                }   // for (int nPoint

                                // calculate interpolation arrays
                                int nTemp = 0;
                                pnCalibrationIndex[nTemp] = 0;
                                pnCalibrationIndex[nLineLength - 1] = nZPLineLength - 2;
                                for (int nPoint = 1; nPoint < nLineLength - 1; nPoint++)
                                {
                                    while ((pdCalibrationZPPoint[nTemp] < (nZPFactor * nPoint)) && (nTemp < nZPLineLength - 1))
                                        nTemp++;
                                    nTemp--;
                                    pnCalibrationIndex[nPoint] = nTemp;
                                }

                            }   // if (UIData.bCalibrate

                            #endregion

                            #region save / load calibration information

                            if (ThreadData.arweProcessingCalibrationSave.WaitOne(0) == true)
                            {
                                pnCalibrationIndex32 = pnCalibrationIndex.Select(Convert.ToInt32).ToArray();
                                pdArray = pnFullImage.Select(Convert.ToDouble).ToArray();
                                string strName = "calibration.dat";

                                //Buffer.BlockCopy(pnCalibrationIndex32, 0, byteBufferCalibrationIndex, 0, byteBufferCalibrationIndex.Length);
                                //FileSystem.WriteAllBytes(strName, byteBufferCalibrationIndex, false);
                                //Buffer.BlockCopy(pdCalibrationZPPoint, 0, byteBufferCalibrationZPPoint, 0, byteBufferCalibrationZPPoint.Length);
                                //FileSystem.WriteAllBytes(strName, byteBufferCalibrationZPPoint, true);
                                //nLineLengthArray[0] = nLineLength;
                                //Buffer.BlockCopy(nLineLengthArray, 0, byteBufferLineLength, 0, byteBufferLineLength.Length);
                                //FileSystem.WriteAllBytes(strName, byteBufferLineLength, true);
                                //Buffer.BlockCopy(pdReference, 0, byteBufferReference, 0, byteBufferReference.Length);
                                //FileSystem.WriteAllBytes(strName, byteBufferReference, true);
                                //nNumberLinesArray[0] = nNumberLines;
                                //Buffer.BlockCopy(nNumberLinesArray, 0, byteBufferNumberLines, 0, byteBufferNumberLines.Length);
                                //FileSystem.WriteAllBytes(strName, byteBufferNumberLines, true);
                                //Buffer.BlockCopy(pdArray, 0, byteArray, 0, byteArray.Length);
                                //FileSystem.WriteAllBytes(strName, byteArray, true);

                                FileStream fs = File.Open(strName, FileMode.Create);
                                BinaryWriter binWriter = new BinaryWriter(fs);
                                for (int nPoint = 0; nPoint < nLineLength; nPoint++)
                                    binWriter.Write(Convert.ToInt32(pnCalibrationIndex[nPoint]));
                                for (int nPoint = 0; nPoint < nZPLineLength; nPoint++)
                                    binWriter.Write(pdCalibrationZPPoint[nPoint]);
                                binWriter.Write(nLineLength);
                                for (int nPoint = 0; nPoint < nLineLength; nPoint++)
                                    binWriter.Write(pdReference[nPoint]);
                                binWriter.Write(nNumberLines);
                                for (int nLine = 0; nLine < nNumberLines; nLine++)
                                    for (int nPoint = 0; nPoint < nLineLength; nPoint++)
                                        binWriter.Write(pdArray[nLine * nLineLength + nPoint]);
                                fs.Close();
                            }   // if (ThreadData.arweProcessingCalibrationSave.WaitOne(0)

                            if (ThreadData.arweProcessingCalibrationLoad.WaitOne(0) == true)
                            {
                                string strName = "calibration.dat";
                                FileStream fs = File.Open(strName, FileMode.Open);
                                BinaryReader binReader = new BinaryReader(fs);
                                for (int nPoint = 0; nPoint < nLineLength; nPoint++)
                                    pnCalibrationIndex[nPoint] = binReader.ReadInt32();
                                for (int nPoint = 0; nPoint < nZPLineLength; nPoint++)
                                    pdCalibrationZPPoint[nPoint] = binReader.ReadDouble();
                                fs.Close();
                            }   // if (ThreadData.arweProcessingCalibrationLoad.WaitOne(0)

                            #endregion

                            #region apply calibration (output: pcdCalibrated)

                            // apply calibration with GPUWrapper
                            GPUWrapper.ApplyCalib(pcdCalibrated, pnFullImage, pdReferenceRepMat, pdCalibrationZPPoint, pnCalibrationIndex, nNumberLines, nLineLength, nZPFactor, nSkipLines);

                            #endregion

                            #region dispersion calculation section

                            if (UIData.bDispersion)
                            {
                                // create mask for depth profiles
                                ComplexDouble[] pcdMask = new ComplexDouble[nLineLength];
                                ComplexDouble[] pcdMasked = new ComplexDouble[nLineLength];
                                double[] pdPhase = new double[nLineLength];

                                Array.Clear(pcdMask, 0, nLineLength);
                                for (int nPoint = 0; nPoint < UIData.nDispersionLeft - UIData.nDispersionRound; nPoint++)
                                    pcdMask[nPoint].Real = 0.0;
                                for (int nPoint = UIData.nDispersionLeft - UIData.nDispersionRound; nPoint < UIData.nDispersionLeft; nPoint++)
                                    pcdMask[nPoint].Real = 0.5 * (1.0 + Math.Cos(Math.PI * Convert.ToDouble(UIData.nDispersionLeft - nPoint) / Convert.ToDouble(UIData.nDispersionRound)));
                                for (int nPoint = UIData.nDispersionLeft; nPoint < UIData.nDispersionRight; nPoint++)
                                    pcdMask[nPoint].Real = 1.0;
                                for (int nPoint = UIData.nDispersionRight; nPoint < UIData.nDispersionRight + UIData.nDispersionRound; nPoint++)
                                    pcdMask[nPoint].Real = 0.5 * (1.0 + Math.Cos(Math.PI * Convert.ToDouble(nPoint - UIData.nDispersionRight) / Convert.ToDouble(UIData.nDispersionRound)));
                                for (int nPoint = UIData.nDispersionRight + UIData.nDispersionRound; nPoint < nLineLength; nPoint++)
                                    pcdMask[nPoint].Real = 0.0;

                                Array.Clear(pdSum, 0, nLineLength);
                                pdZPSum = new double[nZPLineLength];
                                Array.Clear(pdZPSum, 0, nZPLineLength);
                                dLineCount = 0.0;
                                for (int nLine = 0; nLine < nNumberLines; nLine += nSkipLines)
                                {
                                    Array.Copy(pcdCalibrated, nLine * nLineLength, pcdLine, 0, nLineLength);            // copy line from calibrated buffer
                                    pcdFFT = NationalInstruments.Analysis.Dsp.Transforms.Fft(pcdLine, false);           // do fft
                                    for (int nPoint = 0; nPoint < nLineLength / 2; nPoint++)                            // keep track of sum for plot
                                        pdSum[nPoint] += pcdFFT[nPoint].Magnitude * pcdFFT[nPoint].Magnitude;
                                    for (int nPoint = 0; nPoint < nLineLength; nPoint++)                                // apply mask
                                        pcdMasked[nPoint] = pcdFFT[nPoint].Multiply(pcdMask[nPoint]);
                                    pcdLine = NationalInstruments.Analysis.Dsp.Transforms.InverseFft(pcdMasked, false); // inverse fft
                                    for (int nPoint = 0; nPoint < nLineLength; nPoint++)                                // calculate phase
                                        pdPhase[nPoint] = pcdLine[nPoint].Phase;
                                    NationalInstruments.Analysis.Dsp.SignalProcessing.UnwrapPhase(pdPhase);             // unwrap phase
                                    pdZPSum = (pdZPSum.Zip(pdPhase, (x, y) => x + y)).ToArray();                        // keep running sum of phases
                                    dLineCount += 1.0;
                                }
                                double dMin = pdZPSum[0] / dLineCount;
                                double dMax = pdZPSum[nLineLength - 1] / dLineCount;
                                //                        axisDispersionPhaseVertical.Range = new Range<double>(dMin, dMax);
                                double dSlope = (dMax - dMin) / Convert.ToDouble(nLineLength);
                                for (int nPoint = 0; nPoint < nLineLength / 2; nPoint++)
                                {
                                    ThreadData.pdDispersionDepthProfile[0, nPoint] = 10.0 * Math.Log10(pdSum[nPoint] / dLineCount);
                                    ThreadData.pdDispersionDepthProfile[1, nPoint] = 10.0 * Math.Log10((pcdMask[nPoint].Magnitude) * (pcdMask[nPoint].Magnitude) * (pdSum[nPoint] / dLineCount));
                                    ThreadData.pdDispersionPhase[0, nPoint] = pdZPSum[nPoint] / dLineCount;
                                    ThreadData.pdDispersionPhase[1, nPoint] = dMin + dSlope * Convert.ToDouble(nPoint);
                                    pcdDispersionCorrection[nPoint].Real = Math.Cos(ThreadData.pdDispersionPhase[1, nPoint] - ThreadData.pdDispersionPhase[0, nPoint]);
                                    pcdDispersionCorrection[nPoint].Imaginary = Math.Sin(ThreadData.pdDispersionPhase[1, nPoint] - ThreadData.pdDispersionPhase[0, nPoint]);
                                }
                                for (int nPoint = nLineLength / 2; nPoint < nLineLength; nPoint++)
                                {
                                    ThreadData.pdDispersionPhase[0, nPoint] = pdZPSum[nPoint] / dLineCount;
                                    ThreadData.pdDispersionPhase[1, nPoint] = dMin + dSlope * Convert.ToDouble(nPoint);
                                    pcdDispersionCorrection[nPoint].Real = Math.Cos(ThreadData.pdDispersionPhase[1, nPoint] - ThreadData.pdDispersionPhase[0, nPoint]);
                                    pcdDispersionCorrection[nPoint].Imaginary = Math.Sin(ThreadData.pdDispersionPhase[1, nPoint] - ThreadData.pdDispersionPhase[0, nPoint]);

                                    for (int nLine = 0; nLine < nNumberLines; nLine++)
                                    {
                                        pcdDispersionRepMatReal[nLine * nLineLength + nPoint] = pcdDispersionCorrection[nPoint].Real;
                                        pcdDispersionRepMatImag[nLine * nLineLength + nPoint] = pcdDispersionCorrection[nPoint].Imaginary;
                                    }
                                }

                            }   // if (UIData.bDispersion

                            # endregion

                            #region load / save dispersion compensation

                            if (ThreadData.arweProcessingDispersionSave.WaitOne(0) == true)
                            {
                                pdArray = pnFullImage.Select(Convert.ToDouble).ToArray();
                                string strName = "dispersion.dat";

                                //ComplexDouble.DecomposeArray(pcdDispersionCorrection, out pcdDispersionCorrectionR, out pcdDispersionCorrectionI);
                                //Buffer.BlockCopy(pcdDispersionCorrectionR, 0, byteDispersionCorrectionR, 0, byteDispersionCorrectionR.Length);
                                //FileSystem.WriteAllBytes(strName, byteDispersionCorrectionR, false);
                                //Buffer.BlockCopy(pcdDispersionCorrectionI, 0, byteDispersionCorrectionI, 0, byteDispersionCorrectionI.Length);
                                //FileSystem.WriteAllBytes(strName, byteDispersionCorrectionI, true);
                                //nLineLengthArray[0] = nLineLength;
                                //Buffer.BlockCopy(nLineLengthArray, 0, byteBufferLineLength, 0, byteBufferLineLength.Length);
                                //FileSystem.WriteAllBytes(strName, byteBufferLineLength, true);
                                //Buffer.BlockCopy(pdReference, 0, byteBufferReference, 0, byteBufferReference.Length);
                                //FileSystem.WriteAllBytes(strName, byteBufferReference, true);
                                //nNumberLinesArray[0] = nNumberLines;
                                //Buffer.BlockCopy(nNumberLinesArray, 0, byteBufferNumberLines, 0, byteBufferNumberLines.Length);
                                //FileSystem.WriteAllBytes(strName, byteBufferNumberLines, true);
                                //Buffer.BlockCopy(pdArray, 0, byteArray, 0, byteArray.Length);
                                //FileSystem.WriteAllBytes(strName, byteArray, true);

                                FileStream fs = File.Open(strName, FileMode.Create);
                                BinaryWriter binWriter = new BinaryWriter(fs);
                                for (int nPoint = 0; nPoint < nLineLength; nPoint++)
                                {
                                    binWriter.Write(pcdDispersionCorrection[nPoint].Real);
                                    binWriter.Write(pcdDispersionCorrection[nPoint].Imaginary);
                                }
                                binWriter.Write(nLineLength);
                                for (int nPoint = 0; nPoint < nLineLength; nPoint++)
                                    binWriter.Write(pdReference[nPoint]);
                                binWriter.Write(nNumberLines);
                                for (int nLine = 0; nLine < nNumberLines; nLine++)
                                    for (int nPoint = 0; nPoint < nLineLength; nPoint++)
                                        binWriter.Write(pdArray[nLine * nLineLength + nPoint]);
                                fs.Close();
                            }   // if (ThreadData.arweProcessingDispersionSave.WaitOne(0)

                            if (ThreadData.arweProcessingDispersionLoad.WaitOne(0) == true)
                            {
                                string strName = "dispersion.dat";
                                FileStream fs = File.Open(strName, FileMode.Open);
                                BinaryReader binReader = new BinaryReader(fs);
                                for (int nPoint = 0; nPoint < nLineLength; nPoint++)
                                {
                                    pcdDispersionCorrection[nPoint].Real = binReader.ReadDouble();
                                    pcdDispersionCorrection[nPoint].Imaginary = binReader.ReadDouble();

                                    for (int nLine = 0; nLine < nNumberLines; nLine++)
                                    {
                                        pcdDispersionRepMatReal[nLine * nLineLength + nPoint] = pcdDispersionCorrection[nPoint].Real;
                                        pcdDispersionRepMatImag[nLine * nLineLength + nPoint] = pcdDispersionCorrection[nPoint].Imaginary;
                                    }

                                }
                                fs.Close();
                            }   // if (ThreadData.arweProcessingDispersionLoad.WaitOne(0)

                            #endregion

                            #region apply dispersion compensation and convert to depth profiles

                            nIntensityLine = UIData.nIntensityLine;
                            if (nIntensityLine < 0)
                                nIntensityLine = 0;
                            if (nIntensityLine >= nNumberLines)
                                nIntensityLine = nNumberLines - 1;

                            nIntensityPoint = UIData.nIntensityPoint;
                            if (nIntensityPoint < 0)
                                nIntensityPoint = 0;
                            if (nIntensityPoint >= nLineLength / 2)
                                nIntensityPoint = nLineLength / 2 - 1;

                            double[] pcdCalibratedReal = new double[nNumberLines * nLineLength];
                            
                            for (int i = 0; i < nNumberLines * nLineLength; i++)
                            {
                                pcdCalibratedReal[i] = pcdCalibrated[i].Real;
                            }

                            GPUWrapper.ApplyDispComp(ThreadData.pdIntensity, pdPhaseImage, pcdCalibratedReal, pcdDispersionRepMatReal, pcdDispersionRepMatImag, nNumberLines, nLineLength, nSkipLines);
                                                       
                            for (int nPoint = 0; nPoint < nLineLength / 2; nPoint++)
                                UIData.pdIntensityLeft[0, nPoint] = ThreadData.pdIntensity[nIntensityLine, nPoint];

                            for (int nLine = 0; nLine < nNumberLines; nLine++)
                                UIData.pdIntensityTop[0, nLine] = ThreadData.pdIntensity[nLine, nIntensityPoint];

                            #endregion

                            # region variable image

                            if (UIData.bShowVariable)
                            {
                                #region change of variable type
                                if (UIData.nVariableType != ThreadData.nCurrentVariableType)
                                {
                                    switch (UIData.nVariableType)
                                    {
                                        case 0: // en face
                                            ThreadData.pdVariable = new double[nNumberLines, nNumberFrames];
                                            UIData.pdVariableTop = new double[2, nNumberLines];
                                            UIData.pdVariableLeft = new double[2, nNumberFrames];
                                            UIData.pdVariableImage = new double[nNumberLines, nNumberFrames];
                                            // reset cscan frames
                                            Array.Clear(nFramesAcquired, 0, nFramesAcquired.Length);
                                            cFrames = 0;
                                            break;
                                        case 1: // phase
                                            ThreadData.pdVariable = new double[nNumberLines, nLineLength / 2];
                                            UIData.pdVariableTop = new double[2, nNumberLines];
                                            UIData.pdVariableLeft = new double[2, nLineLength / 2];
                                            UIData.pdVariableImage = new double[nNumberLines, nLineLength / 2];
                                            break;
                                    }
                                    ThreadData.arweProcessingVariableChange.Set();
                                    ThreadData.nCurrentVariableType = UIData.nVariableType;
                                }
                                #endregion

                                switch (ThreadData.nCurrentVariableType)
                                {
                                    case 0: // en face
                                        #region calculation
                                        nVariableLine = UIData.nVariableLine;
                                        if (nVariableLine < 0)
                                            nVariableLine = 0;
                                        if (nVariableLine >= nNumberLines)
                                            nVariableLine = nNumberLines - 1;

                                        nVariablePoint = UIData.nVariablePoint;
                                        if (nVariablePoint < 0)
                                            nVariablePoint = 0;
                                        if (nVariablePoint >= nNumberFrames)
                                            nVariablePoint = nNumberFrames;

                                        nEnFaceMinDepth = UIData.nEnFaceMinDepth;
                                        if (nEnFaceMinDepth < 0)
                                            nEnFaceMinDepth = 0;
                                        if (nEnFaceMinDepth >= (nLineLength / 2 - 3))
                                            nEnFaceMinDepth = nLineLength / 2 - 2;

                                        nEnFaceMaxDepth = UIData.nEnFaceMaxDepth;
                                        if (nEnFaceMaxDepth <= nEnFaceMinDepth)
                                            nEnFaceMaxDepth = nEnFaceMinDepth + 1;
                                        if (nEnFaceMaxDepth >= nLineLength / 2)
                                            nEnFaceMaxDepth = nLineLength / 2 - 1;

                                        if (UIData.bOperationPeakTracking)
                                        {
                                            // find peak intensity
                                            int nEnFacePeakDepth = 0;
                                            double nEnFacePeakIntensity = 0;
                                            for (int nPoint = nEnFaceMinDepth; nPoint < nEnFaceMaxDepth; nPoint++)
                                                if (ThreadData.pdIntensity[0, nPoint] > nEnFacePeakIntensity)
                                                {
                                                    nEnFacePeakIntensity = ThreadData.pdIntensity[0, nPoint];
                                                    nEnFacePeakDepth = nPoint;
                                                }
                                            int nEnFaceHalfWidth = 0;
                                            if ((UIData.nEnFacePeakHalfWidth > 0) && (UIData.nEnFacePeakHalfWidth < nLineLength))
                                                nEnFaceHalfWidth = UIData.nEnFacePeakHalfWidth;
                                            nEnFaceMinDepth = nEnFacePeakDepth - nEnFaceHalfWidth;
                                            nEnFaceMaxDepth = nEnFacePeakDepth + nEnFaceHalfWidth;
                                            UIData.nEnFacePeakDepth = nEnFacePeakDepth;

                                            if (nEnFaceMinDepth < 0)
                                                nEnFaceMinDepth = 0;
                                            if (nEnFaceMaxDepth >= nLineLength / 2)
                                                nEnFaceMaxDepth = nLineLength / 2 - 1;
                                        }

                                        double dTemp;
                                        for (int nLine = 0; nLine < nNumberLines; nLine++)
                                        {
                                            dTemp = 0;
                                            for (int nPoint = nEnFaceMinDepth; nPoint < nEnFaceMaxDepth; nPoint++)
                                                dTemp += ThreadData.pdIntensity[nLine, nPoint];
                                            ThreadData.pdVariable[nLine, nFrame - 1] = dTemp / Convert.ToDouble(nEnFaceMaxDepth - nEnFaceMinDepth);
                                        }

                                        for (int nPoint = 0; nPoint < nNumberFrames; nPoint++)
                                            UIData.pdVariableLeft[0, nPoint] = ThreadData.pdVariable[nVariableLine, nPoint];

                                        for (int nLine = 0; nLine < nNumberLines; nLine++)
                                            UIData.pdVariableTop[0, nLine] = ThreadData.pdVariable[nLine, nVariablePoint];

                                        // actually processed this round
                                        nFramesAcquired[nFrame] = true;
                                        cFrames++;
                                        #endregion
                                        break;
                                    case 1: // phase
                                        #region calculation
                                        nVariableLine = UIData.nVariableLine;
                                        if (nVariableLine < 0)
                                            nVariableLine = 0;
                                        if (nVariableLine >= nNumberLines)
                                            nVariableLine = nNumberLines - 1;

                                        nVariablePoint = UIData.nVariablePoint;
                                        if (nVariablePoint < 0)
                                            nVariablePoint = 0;
                                        if (nVariablePoint >= (nLineLength / 2))
                                            nVariablePoint = nLineLength / 2 - 1;

                                        nPhaseReferenceDepth = UIData.nPhaseReferenceDepth;
                                        if (nPhaseReferenceDepth < -1)
                                            nPhaseReferenceDepth = -1;
                                        if (nPhaseReferenceDepth >= (nLineLength / 2 - 1))
                                            nPhaseReferenceDepth = nLineLength / 2 - 1;

                                        if (nPhaseReferenceDepth == -1)
                                        {
                                            for (int nLine = 0; nLine < nNumberLines; nLine++)
                                                for (int nPoint = 0; nPoint < (nLineLength / 2); nPoint++)
                                                    ThreadData.pdVariable[nLine, nPoint] = pdPhaseImage[nLine * (nLineLength / 2) + nPoint];
                                        }
                                        else
                                        {
                                            for (int nLine = 0; nLine < nNumberLines; nLine++)
                                            {
                                                dReference = pdPhaseImage[nLine * (nLineLength / 2) + nPhaseReferenceDepth];
                                                for (int nPoint = 0; nPoint < (nLineLength / 2); nPoint++)
                                                {
                                                    dPhase = pdPhaseImage[nLine * (nLineLength / 2) + nPoint] - dReference;
                                                    if (dPhase > Math.PI)
                                                        dPhase -= 2 * Math.PI;
                                                    if (dPhase < -Math.PI)
                                                        dPhase += 2 * Math.PI;
                                                    ThreadData.pdVariable[nLine, nPoint] = dPhase;
                                                }
                                            }
                                        }

                                        for (int nPoint = 0; nPoint < (nLineLength / 2); nPoint++)
                                            UIData.pdVariableLeft[0, nPoint] = ThreadData.pdVariable[nVariableLine, nPoint];
                                        for (int nLine = 0; nLine < nNumberLines; nLine++)
                                        {
                                            UIData.pdVariableTop[0, nLine] = ThreadData.pdVariable[nLine, nVariablePoint];
                                            pdVariablePhase[nLine] = ThreadData.pdVariable[nLine, nVariablePoint];
                                        }
                                        NationalInstruments.Analysis.Dsp.SignalProcessing.UnwrapPhase(pdVariablePhase);
                                        dReference = 0;
                                        for (int nLine = 0; nLine < nNumberLines; nLine++)
                                            dReference += pdVariablePhase[nLine];
                                        dReference /= Convert.ToDouble(nNumberLines);
                                        for (int nLine = 0; nLine < nNumberLines; nLine++)
                                        {
                                            dPhase = pdVariablePhase[nLine] - dReference;
                                            if (dPhase > Math.PI)
                                                dPhase -= 2 * Math.PI;
                                            if (dPhase < -Math.PI)
                                                dPhase += 2 * Math.PI;
                                            UIData.pdVariableTop[1, nLine] = dPhase;
                                        }

                                        #endregion
                                        break;
                                }
                            }

                            #endregion
                            
                        }
                        else
                        {
                            Thread.Sleep(1);
                        }
                        // do ffts

                        // calculate voltage from pixel
                        UIData.nEnFaceFastAxisVoltage = (UIData.dOperationFastGalvoStop - UIData.dOperationFastGalvoStart) / (nNumberLines - 1) *
                            UIData.nEnFaceFastAxisPixel + UIData.dOperationFastGalvoStart;
                        UIData.nEnFaceSlowAxisVoltage = (UIData.dOperationSlowGalvoStop - UIData.dOperationSlowGalvoStart) / (nNumberFrames - 1) *
                            UIData.nEnFaceSlowAxisPixel + UIData.dOperationSlowGalvoStart;

                        stopwatch_all.Stop();
                        long elapsed_time_all = stopwatch_all.ElapsedMilliseconds;
                        //Debug.WriteLine("Time elapsed in Processing: " + elapsed_time_all.ToString());
                    }
                    
                    // reset c-scan acquisition
                    if (cFrames >= nNumberFrames)
                    {
                        cFrames = 0;
                        Array.Clear(nFramesAcquired, 0, nFramesAcquired.Length);
                    }


                    #region copy DAQ data for viewing
                    for (int nLine = 0; nLine < (UIData.nConfigurationChunksPerImage * UIData.nConfigurationLinesPerChunk); nLine++)
                    {
                        UIData.pdGraphDAQ[0, nLine] = pdLocalDAQ[4 * nLine + 0];
                        UIData.pdGraphDAQ[1, nLine] = pdLocalDAQ[4 * nLine + 1];
                        UIData.pdGraphDAQ[2, nLine] = pdLocalDAQ[4 * nLine + 2];
                        UIData.pdGraphDAQ[3, nLine] = pdLocalDAQ[4 * nLine + 3];
                    }
                    #endregion

                    #region go to next node
                    nodeProcessing = nodeProcessing.Next;
                    if (nodeProcessing == null)
                        nodeProcessing = nodeList.First;
                    #endregion

                }

            }   // while (WaitHandle.WaitAny(pweProcessing)

            ThreadData.strProcessingStatus = "out of main loop";
            // signal that thread is dead
            ThreadData.mrweProcessingThreadDead.Set();

            ThreadData.strProcessingStatus = "dead";
        }

        void CleanupThread()
        {
            ThreadData.strCleanupStatus = "initializing";
            // initialization code goes here
            Thread.Sleep(1000);
            // initialization code goes here

            // node to process
            LinkedListNode<CDataNode> nodeCleanup;
            nodeCleanup = nodeList.First;
            bool bTimeToClean = false;

            // set up wait handles for main loop
            WaitHandle[] pweCleanup = new WaitHandle[2];
            pweCleanup[0] = ThreadData.mrweCleanupThreadKill;
            pweCleanup[1] = ThreadData.mrweCleanupThreadRun;

            // secondary handle array
            WaitHandle[] pweSecondary = new WaitHandle[2];
            pweSecondary[0] = ThreadData.mrweCleanupThreadKill;
            pweSecondary[1] = ThreadData.sweCleanupThreadTrigger;

            // signal that initialization is complete
            ThreadData.mrweCleanupThreadReady.Set();
            ThreadData.strCleanupStatus = "initialized";

            // main loop
            if (WaitHandle.WaitAny(pweCleanup) == 1)
            {
                ThreadData.strCleanupStatus = "in loop";
                while (WaitHandle.WaitAny(pweSecondary) == 1)
                {
                    ThreadData.strCleanupStatus = "semaphore triggered";
                    while (bTimeToClean == false)
                    {
                        // grab mutex
                        nodeCleanup.Value.mut.WaitOne();
                        if (nodeCleanup.Value.bProcessed && nodeCleanup.Value.bSaved)
                        {
                            ThreadData.nCleanupNodeID = nodeCleanup.Value.nID;
                            ThreadData.strCleanupStatus = "cleaning";
                            bTimeToClean = true;
                        }
                        else
                        {
                            nodeCleanup.Value.mut.ReleaseMutex();
                            Thread.Sleep(1);
                        }
                    }

                    // clean node
                    nodeCleanup.Value.bAcquired = false;
                    nodeCleanup.Value.bProcessed = false;
                    nodeCleanup.Value.bSaved = false;
                    bTimeToClean = false;
                    nodeCleanup.Value.mut.ReleaseMutex();

                    // go to next node
                    nodeCleanup = nodeCleanup.Next;
                    if (nodeCleanup == null)
                        nodeCleanup = nodeList.First;

                }   // if (WaitHandle.WaitAny(pweSecondary)
            }   // while (WaitHandle.WaitAny(pweCleanup)

            ThreadData.strCleanupStatus = "out of loop";
            // signal that thread is dead
            ThreadData.mrweCleanupThreadDead.Set();
            ThreadData.strCleanupStatus = "dead";
        }

        private void btnCalibrationLoad_Click(object sender, RoutedEventArgs e)
        {
            ThreadData.arweProcessingCalibrationLoad.Set();
        }

        private void btnCalibrationSave_Click(object sender, RoutedEventArgs e)
        {
            ThreadData.arweProcessingCalibrationSave.Set();
        }

        private void btnDispersionLoad_Click(object sender, RoutedEventArgs e)
        {
            ThreadData.arweProcessingDispersionLoad.Set();
        }

        private void btnDispersionSave_Click(object sender, RoutedEventArgs e)
        {
            ThreadData.arweProcessingDispersionSave.Set();
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

    }

    public class CThreadData
    {
        // primary kernel
        public Thread threadPrimaryKernel;
        public ManualResetEvent mrwePrimaryKernelReady;
        public ManualResetEvent mrwePrimaryKernelRun;
        public ManualResetEvent mrwePrimaryKernelKill;
        public ManualResetEvent mrwePrimaryKernelDead;
        public AutoResetEvent arwePrimaryTrigger;
        public string strPrimaryKernelStatus;
        public int nPrimaryKernelNodeID;
        // WFM thread
        public Thread threadWFM;
        public ManualResetEvent mrweWFMThreadReady;
        public ManualResetEvent mrweWFMThreadRun;
        public ManualResetEvent mrweWFMThreadKill;
        public ManualResetEvent mrweWFMThreadDead;
        public Semaphore sweWFMThreadTrigger;
        public int nWFMSemaphore;
        public int nWFMMaxCount;
        public AutoResetEvent arweWFMUpdate;
        public string strWFMStatus;
        public int nWFMNodeID;
        public double[,] pdWFMInMemory;
        // acquisition thread
        public Thread threadAcquisition;
        public ManualResetEvent mrweAcquisitionThreadReady;
        public ManualResetEvent mrweAcquisitionThreadRun;
        public ManualResetEvent mrweAcquisitionThreadKill;
        public ManualResetEvent mrweAcquisitionThreadDead;
        AutoResetEvent arweAcquisitionTrigger;
        public string strAcquisitionStatus;
        public int nAcquisitionNodeID;
        public double[] pdSpectrum;
        public int nRingDiagnostic1;
        public int nRingDiagnostic2;
        public int nRingDiagnostic3;
        // save thread
        public Thread threadSave;
        public ManualResetEvent mrweSaveThreadReady;
        public ManualResetEvent mrweSaveThreadRun;
        public ManualResetEvent mrweSaveThreadKill;
        public ManualResetEvent mrweSaveThreadDead;
        public Semaphore sweSaveThreadTrigger;
        public int nSaveSemaphore;
        public int nMaxSaveCount;
        public string strSaveStatus;
        public int nSaveNodeID;
        // writing thread
        public Thread threadWrite;
        public ManualResetEvent mrweWriteThreadReady;
        public ManualResetEvent mrweWriteThreadRun;
        public ManualResetEvent mrweWriteThreadKill;
        public ManualResetEvent mrweWriteThreadDead;
        AutoResetEvent arweWriteTrigger;
        public string strWriteStatus;
        // scanning thread
        public Thread threadScan;
        public ManualResetEvent mrweScanThreadReady;
        public ManualResetEvent mrweScanThreadRun;
        public ManualResetEvent mrweScanThreadKill;
        public ManualResetEvent mrweScanThreadDead;
        AutoResetEvent arweScanTrigger;
        public string strScanStatus;
        // processing thread
        public Thread threadProcessing;
        public ManualResetEvent mrweProcessingThreadReady;
        public ManualResetEvent mrweProcessingThreadRun;
        public ManualResetEvent mrweProcessingThreadKill;
        public ManualResetEvent mrweProcessingThreadDead;
        public Semaphore sweProcessingThreadTrigger;
        public int nProcessingSemaphore;
        public string strProcessingStatus;
        public int nProcessingNodeID;
        public double[,] pdIntensity;
        public double[,] pdVariable;
        public AutoResetEvent arweProcessingCalibrationLoad;
        public AutoResetEvent arweProcessingCalibrationSave;
        public AutoResetEvent arweProcessingDispersionLoad;
        public AutoResetEvent arweProcessingDispersionSave;
        public AutoResetEvent arweProcessingVariableChange;
        public int nCurrentVariableType;
        // cleanup thread
        public Thread threadCleanup;
        public ManualResetEvent mrweCleanupThreadReady;
        public ManualResetEvent mrweCleanupThreadRun;
        public ManualResetEvent mrweCleanupThreadKill;
        public ManualResetEvent mrweCleanupThreadDead;
        public Semaphore sweCleanupThreadTrigger;
        public int nCleanupSemaphore;
        public string strCleanupStatus;
        public int nCleanupNodeID;
        public double[,] pdCalibrationPhase;
        public double[,] pdCalibrationDepthProfile;
        public double[,] pdDispersionPhase;
        public double[,] pdDispersionDepthProfile;


        public void Initialize(int nSaveCount, int nWFMCount)
        {
            // primary kernel thread
            mrwePrimaryKernelReady = new ManualResetEvent(false);
            mrwePrimaryKernelRun = new ManualResetEvent(false);
            mrwePrimaryKernelKill = new ManualResetEvent(false);
            mrwePrimaryKernelDead = new ManualResetEvent(false);
            arwePrimaryTrigger = new AutoResetEvent(false);
            strPrimaryKernelStatus = "--";
            nPrimaryKernelNodeID = -1;
            // WFM thread
            mrweWFMThreadReady = new ManualResetEvent(false);
            mrweWFMThreadRun = new ManualResetEvent(false);
            mrweWFMThreadKill = new ManualResetEvent(false);
            mrweWFMThreadDead = new ManualResetEvent(false);
            sweWFMThreadTrigger = new Semaphore(0, nWFMCount);
            nWFMMaxCount = nWFMCount;
            arweWFMUpdate = new AutoResetEvent(false);
            strWFMStatus = "--";
            nWFMNodeID = -1;
            // acquisition thread
            mrweAcquisitionThreadReady = new ManualResetEvent(false);
            mrweAcquisitionThreadRun = new ManualResetEvent(false);
            mrweAcquisitionThreadKill = new ManualResetEvent(false);
            mrweAcquisitionThreadDead = new ManualResetEvent(false);
            arweAcquisitionTrigger = new AutoResetEvent(false);
            strAcquisitionStatus = "--";
            nAcquisitionNodeID = -1;
            // save thread
            mrweSaveThreadReady = new ManualResetEvent(false);
            mrweSaveThreadRun = new ManualResetEvent(false);
            mrweSaveThreadKill = new ManualResetEvent(false);
            mrweSaveThreadDead = new ManualResetEvent(false);
            sweSaveThreadTrigger = new Semaphore(0, nSaveCount);
            nMaxSaveCount = nSaveCount;
            strWFMStatus = "--";
            nWFMNodeID = -1;
            // write thread
            mrweWriteThreadReady = new ManualResetEvent(false);
            mrweWriteThreadRun = new ManualResetEvent(false);
            mrweWriteThreadKill = new ManualResetEvent(false);
            mrweWriteThreadDead = new ManualResetEvent(false);
            arweWriteTrigger = new AutoResetEvent(false);
            strWriteStatus = "--";
            // scan thread
            mrweScanThreadReady = new ManualResetEvent(false);
            mrweScanThreadRun = new ManualResetEvent(false);
            mrweScanThreadKill = new ManualResetEvent(false);
            mrweScanThreadDead = new ManualResetEvent(false);
            arweScanTrigger = new AutoResetEvent(false);
            strScanStatus = "--";
            // processing thread
            mrweProcessingThreadReady = new ManualResetEvent(false);
            mrweProcessingThreadRun = new ManualResetEvent(false);
            mrweProcessingThreadKill = new ManualResetEvent(false);
            mrweProcessingThreadDead = new ManualResetEvent(false);
            sweProcessingThreadTrigger = new Semaphore(0, nSaveCount);
            arweProcessingCalibrationLoad = new AutoResetEvent(false);
            arweProcessingCalibrationSave = new AutoResetEvent(false);
            arweProcessingDispersionLoad = new AutoResetEvent(false);
            arweProcessingDispersionSave = new AutoResetEvent(false);
            arweProcessingVariableChange = new AutoResetEvent(false);
            strProcessingStatus = "--";
            nProcessingNodeID = -1;
            // cleanup thread
            mrweCleanupThreadReady = new ManualResetEvent(false);
            mrweCleanupThreadRun = new ManualResetEvent(false);
            mrweCleanupThreadKill = new ManualResetEvent(false);
            mrweCleanupThreadDead = new ManualResetEvent(false);
            sweCleanupThreadTrigger = new Semaphore(0, nSaveCount);
            strCleanupStatus = "--";
            nCleanupNodeID = -1;
        }   // public void Initialize

        public void Destroy()
        {
            ;
        }   // public void Destroy

    }   // public class CThreadData

    public class CUIData : INotifyPropertyChanged
    {
        public string name_strConfigurationParameterFilename = "strConfigurationParameterFilename";
        private string _strConfigurationParameterFilename;
        public string strConfigurationParameterFilename
        {
            get { return _strConfigurationParameterFilename; }
            set { _strConfigurationParameterFilename = value; OnPropertyChanged(name_strConfigurationParameterFilename); }
        }   // public string strConfigurationParameterFilename

        public string name_strConfigurationParameterSummary = "strConfigurationParameterSummary";
        private string _strConfigurationParameterSummary;
        public string strConfigurationParameterSummary
        {
            get { return _strConfigurationParameterSummary; }
            set { _strConfigurationParameterSummary = value; OnPropertyChanged(name_strConfigurationParameterSummary); }
        }   // public string strConfigurationParameterSummary

        public string name_strConfigurationDAQDevice = "strConfigurationDAQDevice";
        private string _strConfigurationDAQDevice;
        public string strConfigurationDAQDevice
        {
            get { return _strConfigurationDAQDevice; }
            set { _strConfigurationDAQDevice = value; OnPropertyChanged(name_strConfigurationDAQDevice); }
        }   // public string strConfigurationDAQDevice

        public string name_nConfigurationLinesPerSecond = "nConfigurationLinesPerSecond";
        private int _nConfigurationLinesPerSecond;
        public int nConfigurationLinesPerSecond
        {
            get { return _nConfigurationLinesPerSecond; }
            set { _nConfigurationLinesPerSecond = value; OnPropertyChanged(name_nConfigurationLinesPerSecond); }
        }   // public int nConfigurationLinesPerSecond

        public string name_nConfigurationTicksPerLine = "nConfigurationTicksPerLine";
        private int _nConfigurationTicksPerLine;
        public int nConfigurationTicksPerLine
        {
            get { return _nConfigurationTicksPerLine; }
            set { _nConfigurationTicksPerLine = value; OnPropertyChanged(name_nConfigurationTicksPerLine); }
        }   // public int nConfigurationTicksPerLine

        public string name_nConfigurationFramesInWFM = "nConfigurationFramesInWFM";
        private int _nConfigurationFramesInWFM;
        public int nConfigurationFramesInWFM
        {
            get { return _nConfigurationFramesInWFM; }
            set { _nConfigurationFramesInWFM = value; OnPropertyChanged(name_nConfigurationFramesInWFM); }
        }   // public int nConfigurationFramesInWFM

        public string name_bConfigurationIMAQDevice1 = "bConfigurationIMAQDevice1";
        private bool _bConfigurationIMAQDevice1;
        public bool bConfigurationIMAQDevice1
        {
            get { return _bConfigurationIMAQDevice1; }
            set { _bConfigurationIMAQDevice1 = value; OnPropertyChanged(name_bConfigurationIMAQDevice1); }
        }   // public bool bConfigurationIMAQDevice1

        public string name_strConfigurationIMAQDevice1 = "strConfigurationIMAQDevice1";
        private string _strConfigurationIMAQDevice1;
        public string strConfigurationIMAQDevice1
        {
            get { return _strConfigurationIMAQDevice1; }
            set { _strConfigurationIMAQDevice1 = value; OnPropertyChanged(name_strConfigurationIMAQDevice1); }
        }   // public string strConfigurationIMAQDevice1

        public string name_bConfigurationIMAQDevice2 = "bConfigurationIMAQDevice2";
        private bool _bConfigurationIMAQDevice2;
        public bool bConfigurationIMAQDevice2
        {
            get { return _bConfigurationIMAQDevice2; }
            set { _bConfigurationIMAQDevice2 = value; OnPropertyChanged(name_bConfigurationIMAQDevice2); }
        }   // public bool bConfigurationIMAQDevice2

        public string name_strConfigurationIMAQDevice2 = "strConfigurationIMAQDevice2";
        private string _strConfigurationIMAQDevice2;
        public string strConfigurationIMAQDevice2
        {
            get { return _strConfigurationIMAQDevice2; }
            set { _strConfigurationIMAQDevice2 = value; OnPropertyChanged(name_strConfigurationIMAQDevice2); }
        }   // public string strConfigurationIMAQDevice2

        public string name_nConfigurationIMAQLineLength = "nConfigurationIMAQLineLength";
        private int _nConfigurationIMAQLineLength;
        public int nConfigurationIMAQLineLength
        {
            get { return _nConfigurationIMAQLineLength; }
            set { _nConfigurationIMAQLineLength = value; OnPropertyChanged(name_nConfigurationIMAQLineLength); }
        }   // public int nConfigurationIMAQLineLength

        public string name_nConfigurationIMAQRingBufferLength = "nConfigurationIMAQRingBufferLength";
        private int _nConfigurationIMAQRingBufferLength;
        public int nConfigurationIMAQRingBufferLength
        {
            get { return _nConfigurationIMAQRingBufferLength; }
            set { _nConfigurationIMAQRingBufferLength = value; OnPropertyChanged(name_nConfigurationIMAQRingBufferLength); }
        }   // public int nConfigurationIMAQRingBufferLength

        public string name_bConfigurationHSDevice = "bConfigurationHSDevice";
        private bool _bConfigurationHSDevice;
        public bool bConfigurationHSDevice
        {
            get { return _bConfigurationHSDevice; }
            set { _bConfigurationHSDevice = value; OnPropertyChanged(name_bConfigurationHSDevice); }
        }   // public bool bConfigurationHSDevice

        public string name_strConfigurationHSDevice = "strConfigurationHSDevice";
        private string _strConfigurationHSDevice;
        public string strConfigurationHSDevice
        {
            get { return _strConfigurationHSDevice; }
            set { _strConfigurationHSDevice = value; OnPropertyChanged(name_strConfigurationHSDevice); }
        }   // public string strConfigurationHSDevice

        public string name_bConfigurationHSChannel1 = "bConfigurationHSChannel1";
        private bool _bConfigurationHSChannel1;
        public bool bConfigurationHSChannel1
        {
            get { return _bConfigurationHSChannel1; }
            set { _bConfigurationHSChannel1 = value; OnPropertyChanged(name_bConfigurationHSChannel1); }
        }   // public bool bConfigurationHSChannel1

        public string name_bConfigurationHSChannel2 = "bConfigurationHSChannel2";
        private bool _bConfigurationHSChannel2;
        public bool bConfigurationHSChannel2
        {
            get { return _bConfigurationHSChannel2; }
            set { _bConfigurationHSChannel2 = value; OnPropertyChanged(name_bConfigurationHSChannel2); }
        }   // public bool bConfigurationHSChannel2

        public string name_nConfigurationHSLineLength = "nConfigurationHSLineLength";
        private int _nConfigurationHSLineLength;
        public int nConfigurationHSLineLength
        {
            get { return _nConfigurationHSLineLength; }
            set { _nConfigurationHSLineLength = value; OnPropertyChanged(name_nConfigurationHSLineLength); }
        }   // public int nConfigurationHSLineLength

        public string name_nConfigurationLinesPerChunk = "nConfigurationLinesPerChunk";
        private int _nConfigurationLinesPerChunk;
        public int nConfigurationLinesPerChunk
        {
            get { return _nConfigurationLinesPerChunk; }
            set { _nConfigurationLinesPerChunk = value; OnPropertyChanged(name_nConfigurationLinesPerChunk); }
        }   // public int nConfigurationLinesPerChunk

        public string name_nConfigurationChunksPerImage = "nConfigurationChunksPerImage";
        private int _nConfigurationChunksPerImage;
        public int nConfigurationChunksPerImage
        {
            get { return _nConfigurationChunksPerImage; }
            set { _nConfigurationChunksPerImage = value; OnPropertyChanged(name_nConfigurationChunksPerImage); }
        }   // public int nConfigurationChunksPerImage

        public string name_nConfigurationPreIgnoreChunks = "nConfigurationPreIgnoreChunks";
        private int _nConfigurationPreIgnoreChunks;
        public int nConfigurationPreIgnoreChunks
        {
            get { return _nConfigurationPreIgnoreChunks; }
            set { _nConfigurationPreIgnoreChunks = value; OnPropertyChanged(name_nConfigurationPreIgnoreChunks); }
        }   // public int nConfigurationPreIgnoreChunks

        public string name_nConfigurationPostIgnoreChunks = "nConfigurationPostIgnoreChunks";
        private int _nConfigurationPostIgnoreChunks;
        public int nConfigurationPostIgnoreChunks
        {
            get { return _nConfigurationPostIgnoreChunks; }
            set { _nConfigurationPostIgnoreChunks = value; OnPropertyChanged(name_nConfigurationPostIgnoreChunks); }
        }   // public int nConfigurationPostIgnoreChunks

        public string name_nConfigurationImagesPerVolume = "nConfigurationImagesPerVolume";
        private int _nConfigurationImagesPerVolume;
        public int nConfigurationImagesPerVolume
        {
            get { return _nConfigurationImagesPerVolume; }
            set { _nConfigurationImagesPerVolume = value; OnPropertyChanged(name_nConfigurationImagesPerVolume); }
        }   // public int nConfigurationImagesPerVolume

        public string name_nConfigurationLinkedListLength = "nConfigurationLinkedListLength";
        private int _nConfigurationLinkedListLength;
        public int nConfigurationLinkedListLength
        {
            get { return _nConfigurationLinkedListLength; }
            set { _nConfigurationLinkedListLength = value; OnPropertyChanged(name_nConfigurationLinkedListLength); }
        }   // public int nConfigurationLinkedListLength

        public string name_strOperationFileDirectory = "strOperationFileDirectory";
        private string _strOperationFileDirectory;
        public string strOperationFileDirectory
        {
            get { return _strOperationFileDirectory; }
            set { _strOperationFileDirectory = value; OnPropertyChanged(name_strOperationFileDirectory); }
        }   // public string strOperationFileDirectory

        public string name_strOperationFilePrefix = "strOperationFilePrefix";
        private string _strOperationFilePrefix;
        public string strOperationFilePrefix
        {
            get { return _strOperationFilePrefix; }
            set { _strOperationFilePrefix = value; OnPropertyChanged(name_strOperationFilePrefix); }
        }   // public string strOperationFilePrefix

        public string name_nOperationFileNumber = "nOperationFileNumber";
        private int _nOperationFileNumber;
        public int nOperationFileNumber
        {
            get { return _nOperationFileNumber; }
            set { _nOperationFileNumber = value; OnPropertyChanged(name_nOperationFileNumber); }
        }   // public int nOperationFileNumber

        public string name_bOperationFileRecord = "bOperationFileRecord";
        private bool _bOperationFileRecord;
        public bool bOperationFileRecord
        {
            get { return _bOperationFileRecord; }
            set { _bOperationFileRecord = value; OnPropertyChanged(name_bOperationFileRecord); }
        }   // public bool bOperationFileRecord

        public string name_bOperationPeakTracking = "bOperationPeakTracking";
        private bool _bOperationPeakTracking;
        public bool bOperationPeakTracking
        {
            get { return _bOperationPeakTracking; }
            set { _bOperationPeakTracking = value; OnPropertyChanged(name_bOperationPeakTracking); }
        }   // public bool bOperationPeakTracking

        public string name_bOperationPhaseScanning = "bOperationPhaseScanning";
        private bool _bOperationPhaseScanning;
        public bool bOperationPhaseScanning
        {
            get { return _bOperationPhaseScanning; }
            set { _bOperationPhaseScanning = value; OnPropertyChanged(name_bOperationPhaseScanning); }
        }   // public bool bOperationPhaseScanning

        public string name_dOperationFastGalvoStart = "dOperationFastGalvoStart";
        private double _dOperationFastGalvoStart;
        public double dOperationFastGalvoStart
        {
            get { return _dOperationFastGalvoStart; }
            set { _dOperationFastGalvoStart = value; OnPropertyChanged(name_dOperationFastGalvoStart); }
        }   // public double nOperationFastGalvoStart

        public string name_dOperationFastGalvoStop = "dOperationFastGalvoStop";
        private double _dOperationFastGalvoStop;
        public double dOperationFastGalvoStop
        {
            get { return _dOperationFastGalvoStop; }
            set { _dOperationFastGalvoStop = value; OnPropertyChanged(name_dOperationFastGalvoStop); }
        }   // public double nOperationFastGalvoStop

        public string name_nOperationFastGalvoLinesPerPosition = "nOperationFastGalvoLinesPerPosition";
        private int _nOperationFastGalvoLinesPerPosition;
        public int nOperationFastGalvoLinesPerPosition
        {
            get { return _nOperationFastGalvoLinesPerPosition; }
            set { _nOperationFastGalvoLinesPerPosition = value; OnPropertyChanged(name_nOperationFastGalvoLinesPerPosition); }
        }   // public int nOperationFastGalvoLinesPerPosition

        public string name_dOperationSlowGalvoStart = "dOperationSlowGalvoStart";
        private double _dOperationSlowGalvoStart;
        public double dOperationSlowGalvoStart
        {
            get { return _dOperationSlowGalvoStart; }
            set { _dOperationSlowGalvoStart = value; OnPropertyChanged(name_dOperationSlowGalvoStart); }
        }   // public double nOperationSlowGalvoStart

        public string name_dOperationSlowGalvoStop = "dOperationSlowGalvoStop";
        private double _dOperationSlowGalvoStop;
        public double dOperationSlowGalvoStop
        {
            get { return _dOperationSlowGalvoStop; }
            set { _dOperationSlowGalvoStop = value; OnPropertyChanged(name_dOperationSlowGalvoStop); }
        }   // public double nOperationSlowGalvoStop

        public string name_dOperationCenterX = "dOperationCenterX";
        private double _dOperationCenterX;
        public double dOperationCenterX
        {
            get { return _dOperationCenterX; }
            set { _dOperationCenterX = value; OnPropertyChanged(name_dOperationCenterX); }
        }   // public double nOperationCenterX

        public string name_dOperationCenterY = "dOperationCenterY";
        private double _dOperationCenterY;
        public double dOperationCenterY
        {
            get { return _dOperationCenterY; }
            set { _dOperationCenterY = value; OnPropertyChanged(name_dOperationCenterY); }
        }   // public double nOperationCenterY

        public string name_dOperationCenterAngle = "dOperationCenterAngle";
        private double _dOperationCenterAngle;
        public double dOperationCenterAngle
        {
            get { return _dOperationCenterAngle; }
            set { _dOperationCenterAngle = value; OnPropertyChanged(name_dOperationCenterAngle); }
        }   // public double nOperationCenterAngle

        public string name_dOperationScanWidth = "dOperationScanWidth";
        private double _dOperationScanWidth;
        public double dOperationScanWidth
        {
            get { return _dOperationScanWidth; }
            set { _dOperationScanWidth = value; OnPropertyChanged(name_dOperationScanWidth); }
        }   // public double nOperationScanWidth

        public string name_nOperationSlowGalvoCurrentFrame = "nOperationSlowGalvoCurrentFrame";
        private int _nOperationSlowGalvoCurrentFrame;
        public int nOperationSlowGalvoCurrentFrame
        {
            get { return _nOperationSlowGalvoCurrentFrame; }
            set { _nOperationSlowGalvoCurrentFrame = value; OnPropertyChanged(name_nOperationSlowGalvoCurrentFrame); }
        }   // public int nOperationSlowGalvoCurrentFrame

        public string name_strDiagnosticsPrimaryKernelStatus = "strDiagnosticsPrimaryKernelStatus";
        private string _strDiagnosticsPrimaryKernelStatus;
        public string strDiagnosticsPrimaryKernelStatus
        {
            get { return _strDiagnosticsPrimaryKernelStatus; }
            set { _strDiagnosticsPrimaryKernelStatus = value; OnPropertyChanged(name_strDiagnosticsPrimaryKernelStatus); }
        }   // public string strDiagnosticsPrimaryKernelStatus

        public string name_strDiagnosticsWFMThreadStatus = "strDiagnosticsWFMThreadStatus";
        private string _strDiagnosticsWFMThreadStatus;
        public string strDiagnosticsWFMThreadStatus
        {
            get { return _strDiagnosticsWFMThreadStatus; }
            set { _strDiagnosticsWFMThreadStatus = value; OnPropertyChanged(name_strDiagnosticsWFMThreadStatus); }
        }   // public string strDiagnosticsWFMThreadStatus

        public string name_strDiagnosticsAcquisitionThreadStatus = "strDiagnosticsAcquisitionThreadStatus";
        private string _strDiagnosticsAcquisitionThreadStatus;
        public string strDiagnosticsAcquisitionThreadStatus
        {
            get { return _strDiagnosticsAcquisitionThreadStatus; }
            set { _strDiagnosticsAcquisitionThreadStatus = value; OnPropertyChanged(name_strDiagnosticsAcquisitionThreadStatus); }
        }   // public string strDiagnosticsAcquisitionThreadStatus

        public string name_strDiagnosticsSaveThreadStatus = "strDiagnosticsSaveThreadStatus";
        private string _strDiagnosticsSaveThreadStatus;
        public string strDiagnosticsSaveThreadStatus
        {
            get { return _strDiagnosticsSaveThreadStatus; }
            set { _strDiagnosticsSaveThreadStatus = value; OnPropertyChanged(name_strDiagnosticsSaveThreadStatus); }
        }   // public string strDiagnosticsSaveThreadStatus

        public string name_strDiagnosticsWriteThreadStatus = "strDiagnosticsWriteThreadStatus";
        private string _strDiagnosticsWriteThreadStatus;
        public string strDiagnosticsWriteThreadStatus
        {
            get { return _strDiagnosticsWriteThreadStatus; }
            set { _strDiagnosticsWriteThreadStatus = value; OnPropertyChanged(name_strDiagnosticsWriteThreadStatus); }
        }   // public string strDiagnosticsWriteThreadStatus

        public string name_strDiagnosticsProcessingThreadStatus = "strDiagnosticsProcessingThreadStatus";
        private string _strDiagnosticsProcessingThreadStatus;
        public string strDiagnosticsProcessingThreadStatus
        {
            get { return _strDiagnosticsProcessingThreadStatus; }
            set { _strDiagnosticsProcessingThreadStatus = value; OnPropertyChanged(name_strDiagnosticsProcessingThreadStatus); }
        }   // public string strDiagnosticsProcessingThreadStatus

        public string name_strDiagnosticsCleanupThreadStatus = "strDiagnosticsCleanupThreadStatus";
        private string _strDiagnosticsCleanupThreadStatus;
        public string strDiagnosticsCleanupThreadStatus
        {
            get { return _strDiagnosticsCleanupThreadStatus; }
            set { _strDiagnosticsCleanupThreadStatus = value; OnPropertyChanged(name_strDiagnosticsCleanupThreadStatus); }
        }   // public string strDiagnosticsCleanupThreadStatus

        public string name_nDiagnosticsNodeID = "nDiagnosticsNodeID";
        private int _nDiagnosticsNodeID;
        public int nDiagnosticsNodeID
        {
            get { return _nDiagnosticsNodeID; }
            set { _nDiagnosticsNodeID = value; OnPropertyChanged(name_nDiagnosticsNodeID); }
        }   // public int nDiagnosticsNodeID

        public string name_bDiagnosticsNodeAcquisition = "bDiagnosticsNodeAcquisition";
        private bool _bDiagnosticsNodeAcquisition;
        public bool bDiagnosticsNodeAcquisition
        {
            get { return _bDiagnosticsNodeAcquisition; }
            set { _bDiagnosticsNodeAcquisition = value; OnPropertyChanged(name_bDiagnosticsNodeAcquisition); }
        }   // public bool bDiagnosticsNodeAcquisition

        public string name_bDiagnosticsNodeSave = "bDiagnosticsNodeSave";
        private bool _bDiagnosticsNodeSave;
        public bool bDiagnosticsNodeSave
        {
            get { return _bDiagnosticsNodeSave; }
            set { _bDiagnosticsNodeSave = value; OnPropertyChanged(name_bDiagnosticsNodeSave); }
        }   // public bool bDiagnosticsNodeSave

        public string name_bDiagnosticsNodeProcessing = "bDiagnosticsNodeProcessing";
        private bool _bDiagnosticsNodeProcessing;
        public bool bDiagnosticsNodeProcessing
        {
            get { return _bDiagnosticsNodeProcessing; }
            set { _bDiagnosticsNodeProcessing = value; OnPropertyChanged(name_bDiagnosticsNodeProcessing); }
        }   // public bool bDiagnosticsNodeProcessing

        public string name_nSpectrumLineNumber = "nSpectrumLineNumber";
        private int _nSpectrumLineNumber;
        public int nSpectrumLineNumber
        {
            get { return _nSpectrumLineNumber; }
            set { _nSpectrumLineNumber = value; OnPropertyChanged(name_nSpectrumLineNumber); }
        }

        public string name_bShowIntensity = "bShowIntensity";
        private bool _bShowIntensity;
        public bool bShowIntensity
        {
            get { return _bShowIntensity; }
            set { _bShowIntensity = value; OnPropertyChanged(name_bShowIntensity); }
        }

        public string name_nConfigurationZPFactor = "nConfigurationZPFactor";
        private int _nConfigurationZPFactor;
        public int nConfigurationZPFactor
        {
            get { return _nConfigurationZPFactor; }
            set { _nConfigurationZPFactor = value; OnPropertyChanged(name_nConfigurationZPFactor); }
        }

        // 20210712 HY Beginning //
        public string name_nDataFileNumber = "nDataFileNumber";
        private int _nDataFileNumber;
        public int nDataFileNumber
        {
            get { return _nDataFileNumber; }
            set { _nDataFileNumber = value; OnPropertyChanged(name_nDataFileNumber); }
        }

        // 20210712 HY End //


        public string name_nReferenceType;
        private int _nReferenceType;
        public int nReferenceType
        {
            get { return _nReferenceType; }
            set { _nReferenceType = value; OnPropertyChanged(name_nReferenceType); }
        }

        public string name_nSpectrumLine = "nSpectrumLine";
        private int _nSpectrumLine;
        public int nSpectrumLine
        {
            get { return _nSpectrumLine; }
            set { _nSpectrumLine = value; OnPropertyChanged(name_nSpectrumLine); }
        }

        public string name_nSpectrumColorScaleMax = "nSpectrumColorScaleMax";
        private int _nSpectrumColorScaleMax;
        public int nSpectrumColorScaleMax
        {
            get { return _nSpectrumColorScaleMax; }
            set { _nSpectrumColorScaleMax = value; OnPropertyChanged(name_nSpectrumColorScaleMax); }
        }

        public string name_nSpectrumColorScaleMin = "nSpectrumColorScaleMin";
        private int _nSpectrumColorScaleMin;
        public int nSpectrumColorScaleMin
        {
            get { return _nSpectrumColorScaleMin; }
            set { _nSpectrumColorScaleMin = value; OnPropertyChanged(name_nSpectrumColorScaleMin); }
        }

        public string name_dCalibrationMax = "dCalibrationMax";
        private double _dCalibrationMax;
        public double dCalibrationMax
        {
            get { return _dCalibrationMax; }
            set { _dCalibrationMax = value; OnPropertyChanged(name_dCalibrationMax); }
        }

        public string name_dCalibrationMin = "dCalibrationMin";
        private double _dCalibrationMin;
        public double dCalibrationMin
        {
            get { return _dCalibrationMin; }
            set { _dCalibrationMin = value; OnPropertyChanged(name_dCalibrationMin); }
        }

        public string name_nCalibrationLeft = "nCalibrationLeft";
        private int _nCalibrationLeft;
        public int nCalibrationLeft
        {
            get { return _nCalibrationLeft; }
            set { _nCalibrationLeft = value; OnPropertyChanged(name_nCalibrationLeft); }
        }

        public string name_nCalibrationRight = "nCalibrationRight";
        private int _nCalibrationRight;
        public int nCalibrationRight
        {
            get { return _nCalibrationRight; }
            set { _nCalibrationRight = value; OnPropertyChanged(name_nCalibrationRight); }
        }

        public string name_nCalibrationRound = "nCalibrationRound";
        private int _nCalibrationRound;
        public int nCalibrationRound
        {
            get { return _nCalibrationRound; }
            set { _nCalibrationRound = value; OnPropertyChanged(name_nCalibrationRound); }
        }

        public string name_bCalibrate = "bCalibrate";
        private bool _bCalibrate;
        public bool bCalibrate
        {
            get { return _bCalibrate; }
            set { _bCalibrate = value; OnPropertyChanged(name_bCalibrate); }
        }

        public string name_dDispersionMax = "dDispersionMax";
        private double _dDispersionMax;
        public double dDispersionMax
        {
            get { return _dDispersionMax; }
            set { _dDispersionMax = value; OnPropertyChanged(name_dDispersionMax); }
        }

        public string name_dDispersionMin = "dDispersionMin";
        private double _dDispersionMin;
        public double dDispersionMin
        {
            get { return _dDispersionMin; }
            set { _dDispersionMin = value; OnPropertyChanged(name_dDispersionMin); }
        }

        public string name_nDispersionLeft = "nDispersionLeft";
        private int _nDispersionLeft;
        public int nDispersionLeft
        {
            get { return _nDispersionLeft; }
            set { _nDispersionLeft = value; OnPropertyChanged(name_nDispersionLeft); }
        }

        public string name_nDispersionRight = "nDispersionRight";
        private int _nDispersionRight;
        public int nDispersionRight
        {
            get { return _nDispersionRight; }
            set { _nDispersionRight = value; OnPropertyChanged(name_nDispersionRight); }
        }

        public string name_nDispersionRound = "nDispersionRound";
        private int _nDispersionRound;
        public int nDispersionRound
        {
            get { return _nDispersionRound; }
            set { _nDispersionRound = value; OnPropertyChanged(name_nDispersionRound); }
        }

        public string name_bDispersion = "bDispersion";
        private bool _bDispersion;
        public bool bDispersion
        {
            get { return _bDispersion; }
            set { _bDispersion = value; OnPropertyChanged(name_bDispersion); }
        }

        public string name_nSkipLines = "nSkipLines";
        private int _nSkipLines;
        public int nSkipLines
        {
            get { return _nSkipLines; }
            set { _nSkipLines = value; OnPropertyChanged(name_nSkipLines); }
        }

        public string name_nIntensityLine = "nIntensityLine";
        private int _nIntensityLine;
        public int nIntensityLine
        {
            get { return _nIntensityLine; }
            set { _nIntensityLine = value; OnPropertyChanged(name_nIntensityLine); }
        }

        public string name_nIntensityPoint = "nIntensityPoint";
        private int _nIntensityPoint;
        public int nIntensityPoint
        {
            get { return _nIntensityPoint; }
            set { _nIntensityPoint = value; OnPropertyChanged(name_nIntensityPoint); }
        }

        public string name_nRingDiagnostic1 = "nRingDiagnostic1";
        private int _nRingDiagnostic1;
        public int nRingDiagnostic1
        {
            get { return _nRingDiagnostic1; }
            set { _nRingDiagnostic1 = value; OnPropertyChanged(name_nRingDiagnostic1); }
        }

        public string name_nRingDiagnostic2 = "nRingDiagnostic2";
        private int _nRingDiagnostic2;
        public int nRingDiagnostic2
        {
            get { return _nRingDiagnostic2; }
            set { _nRingDiagnostic2 = value; OnPropertyChanged(name_nRingDiagnostic2); }
        }

        public string name_nRingDiagnostic3 = "nRingDiagnostic3";
        private int _nRingDiagnostic3;
        public int nRingDiagnostic3
        {
            get { return _nRingDiagnostic3; }
            set { _nRingDiagnostic3 = value; OnPropertyChanged(name_nRingDiagnostic3); }
        }

        public string name_bShowVariable;
        private bool _bShowVariable;
        public bool bShowVariable
        {
            get { return _bShowVariable; }
            set { _bShowVariable = value; OnPropertyChanged(name_bShowVariable); }
        }

        public string name_nVariableLine = "nVariableLine";
        private int _nVariableLine;
        public int nVariableLine
        {
            get { return _nVariableLine; }
            set { _nVariableLine = value; OnPropertyChanged(name_nVariableLine); }
        }

        public string name_nVariablePoint = "nVariablePoint";
        private int _nVariablePoint;
        public int nVariablePoint
        {
            get { return _nVariablePoint; }
            set { _nVariablePoint = value; OnPropertyChanged(name_nVariablePoint); }
        }

        public string name_nVariableType = "nVariableType";
        private int _nVariableType;
        public int nVariableType
        {
            get { return _nVariableType; }
            set { _nVariableType = value; OnPropertyChanged(name_nVariableType); }
        }

        public string name_nEnFaceMinDepth = "nEnFaceMinDepth";
        private int _nEnFaceMinDepth;
        public int nEnFaceMinDepth
        {
            get { return _nEnFaceMinDepth; }
            set { _nEnFaceMinDepth = value; OnPropertyChanged(name_nEnFaceMinDepth); }
        }

        public string name_nEnFaceMaxDepth = "EnFaceMaxDepth";
        private int _nEnFaceMaxDepth;
        public int nEnFaceMaxDepth
        {
            get { return _nEnFaceMaxDepth; }
            set { _nEnFaceMaxDepth = value; OnPropertyChanged(name_nEnFaceMaxDepth); }
        }

        public string name_nEnFaceFastAxisPixel = "nEnFaceFastAxisPixel";
        private int _nEnFaceFastAxisPixel;
        public int nEnFaceFastAxisPixel
        {
            get { return _nEnFaceFastAxisPixel; }
            set { _nEnFaceFastAxisPixel = value; OnPropertyChanged(name_nEnFaceFastAxisPixel); }
        }

        public string name_nEnFaceFastAxisVoltage = "nEnFaceFastAxisVoltage";
        private double _nEnFaceFastAxisVoltage;
        public double nEnFaceFastAxisVoltage
        {
            get { return _nEnFaceFastAxisVoltage; }
            set { _nEnFaceFastAxisVoltage = value; OnPropertyChanged(name_nEnFaceFastAxisVoltage); }
        }

        public string name_nEnFaceSlowAxisPixel = "nEnFaceSlowAxisPixel";
        private int _nEnFaceSlowAxisPixel;
        public int nEnFaceSlowAxisPixel
        {
            get { return _nEnFaceSlowAxisPixel; }
            set { _nEnFaceSlowAxisPixel = value; OnPropertyChanged(name_nEnFaceSlowAxisPixel); }
        }

        public string name_nEnFaceSlowAxisVoltage = "nEnFaceSlowAxisVoltage";
        private double _nEnFaceSlowAxisVoltage;
        public double nEnFaceSlowAxisVoltage
        {
            get { return _nEnFaceSlowAxisVoltage; }
            set { _nEnFaceSlowAxisVoltage = value; OnPropertyChanged(name_nEnFaceSlowAxisVoltage); }
        }

        public string name_nEnFacePeakDepth = "nEnFacePeakDepth";
        private int _nEnFacePeakDepth;
        public int nEnFacePeakDepth
        {
            get { return _nEnFacePeakDepth; }
            set { _nEnFacePeakDepth = value; OnPropertyChanged(name_nEnFacePeakDepth); }
        }

        public string name_nEnFacePeakHalfWidth = "nEnFacePeakHalfWidth";
        private int _nEnFacePeakHalfWidth;
        public int nEnFacePeakHalfWidth
        {
            get { return _nEnFacePeakHalfWidth; }
            set { _nEnFacePeakHalfWidth = value; OnPropertyChanged(name_nEnFacePeakHalfWidth); }
        }

        public string name_nPhaseReferenceDepth = "nPhaseReferenceDepth";
        private int _nPhaseReferenceDepth;
        public int nPhaseReferenceDepth
        {
            get { return _nPhaseReferenceDepth; }
            set { _nPhaseReferenceDepth = value; OnPropertyChanged(name_nPhaseReferenceDepth); }
        }

        public string name_dPhaseScanFastGalvoStart = "dPhaseScanFastGalvoStart";
        private double _dPhaseScanFastGalvoStart;
        public double dPhaseScanFastGalvoStart
        {
            get { return _dPhaseScanFastGalvoStart; }
            set { _dPhaseScanFastGalvoStart = value; OnPropertyChanged(name_dPhaseScanFastGalvoStart); }
        }

        public string name_dPhaseScanFastGalvoStop = "dPhaseScanFastGalvoStop";
        private double _dPhaseScanFastGalvoStop;
        public double dPhaseScanFastGalvoStop
        {
            get { return _dPhaseScanFastGalvoStop; }
            set { _dPhaseScanFastGalvoStop = value; OnPropertyChanged(name_dPhaseScanFastGalvoStop); }
        }

        public string name_nPhaseScanFastGalvoSteps = "nPhaseScanFastGalvoSteps";
        private int _nPhaseScanFastGalvoSteps;
        public int nPhaseScanFastGalvoSteps
        {
            get { return _nPhaseScanFastGalvoSteps; }
            set { _nPhaseScanFastGalvoSteps = value; OnPropertyChanged(name_nPhaseScanFastGalvoSteps); }
        }

        public string name_dPhaseScanSlowGalvoStart = "dPhaseScanSlowGalvoStart";
        private double _dPhaseScanSlowGalvoStart;
        public double dPhaseScanSlowGalvoStart
        {
            get { return _dPhaseScanSlowGalvoStart; }
            set { _dPhaseScanSlowGalvoStart = value; OnPropertyChanged(name_dPhaseScanSlowGalvoStart); }
        }

        public string name_dPhaseScanSlowGalvoStop = "dPhaseScanSlowGalvoStop";
        private double _dPhaseScanSlowGalvoStop;
        public double dPhaseScanSlowGalvoStop
        {
            get { return _dPhaseScanSlowGalvoStop; }
            set { _dPhaseScanSlowGalvoStop = value; OnPropertyChanged(name_dPhaseScanSlowGalvoStop); }
        }

        public string name_nPhaseScanSlowGalvoSteps = "nPhaseScanSlowGalvoSteps";
        private int _nPhaseScanSlowGalvoSteps;
        public int nPhaseScanSlowGalvoSteps
        {
            get { return _nPhaseScanSlowGalvoSteps; }
            set { _nPhaseScanSlowGalvoSteps = value; OnPropertyChanged(name_nPhaseScanSlowGalvoSteps); }
        }

        public string name_nPhaseScanImagesPerSpot = "nPhaseScanImagesPerSpot";
        private int _nPhaseScanImagesPerSpot;
        public int nPhaseScanImagesPerSpot
        {
            get { return _nPhaseScanImagesPerSpot; }
            set { _nPhaseScanImagesPerSpot = value; OnPropertyChanged(name_nPhaseScanImagesPerSpot); }
        }

        public string name_nPhaseScanRestingTime = "nPhaseScanRestingTime";
        private int _nPhaseScanRestingTime;
        public int nPhaseScanRestingTime
        {
            get { return _nPhaseScanRestingTime; }
            set { _nPhaseScanRestingTime = value; OnPropertyChanged(name_nPhaseScanRestingTime); }
        }

        public string name_strPhaseScanFilePrefix = "strPhaseScanFilePrefix";
        private string _strPhaseScanFilePrefix;
        public string strPhaseScanFilePrefix
        {
            get { return _strPhaseScanFilePrefix; }
            set { _strPhaseScanFilePrefix = value; OnPropertyChanged(name_strPhaseScanFilePrefix); }
        }   // public string strPhaseScanFilePrefix

        public LinkedListNode<CDataNode> nodeDiagnostics;
        public int[,] pnGraphDiagnosticsLinkedList;

        public double[,] pdGraphDAQ;
        public double[,] pdSpectrum;

        public double[,] pdIntensityImage;
        public double[,] pdIntensityTop;
        public double[,] pdIntensityLeft;

        public double[,] pdVariableImage;
        public double[,] pdVariableTop;
        public double[,] pdVariableLeft;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }   // 
        }   // protected void OnPropertyChanged
    }   // public class CUIData

    public class CDataNode
    {
        public int nID;
        public string strFilename;
        public bool bRecord;
        public int nFrameNumber;
        // data structures
        public int nLineLength;
        public int nNumberChunks;
        public int nNumberLinesPerChunk;
        public Int16[][] pnIMAQ;
        public double[] pnDAQ;
        // status flags
        public Mutex mut;
        public bool bAcquired;
        public bool bSaved;
        public bool bProcessed;

        public CDataNode(int nodenumber, int linelength, int numberchunks, int numberClines, bool channel1, bool channel2)
        {
            nID = nodenumber;
            strFilename = "";
            bRecord = false;
            nFrameNumber = 0;
            // data structures
            nLineLength = linelength;
            nNumberChunks = numberchunks;
            nNumberLinesPerChunk = numberClines;
            pnIMAQ = new Int16[nNumberChunks][];
            for (int nChunk = 0; nChunk < nNumberChunks; nChunk++)
                pnIMAQ[nChunk] = new Int16[nLineLength * nNumberLinesPerChunk];
            pnDAQ = new double[4 * nNumberChunks * nNumberLinesPerChunk];  // 4 channels of data
            // status flags
            mut = new Mutex();
            bAcquired = false;
            bSaved = false;
            bProcessed = false;
        }   // public CDataNode

        public CDataNode(CDataNode src)     // new construct function for creating a new node from an existing one
        {
            nID = src.nID;
            strFilename = src.strFilename;
            bRecord = src.bRecord;
            nFrameNumber = src.nFrameNumber;
            nLineLength = src.nLineLength;
            nNumberChunks = src.nNumberChunks;
            nNumberLinesPerChunk = src.nNumberLinesPerChunk;
            pnIMAQ = new Int16[nNumberChunks][];
            for (int nChunk = 0; nChunk < nNumberChunks; nChunk++)
            {
                pnIMAQ[nChunk] = new Int16[nLineLength * nNumberLinesPerChunk];
                Array.Copy(src.pnIMAQ[nChunk], pnIMAQ[nChunk], nLineLength * nNumberLinesPerChunk);
            }
            pnDAQ = new double[4 * nNumberChunks * nNumberLinesPerChunk];
            Array.Copy(src.pnDAQ, pnDAQ, 4 * nNumberChunks * nNumberLinesPerChunk);
            mut = new Mutex();
            bAcquired = src.bAcquired;
            bSaved = src.bSaved;
            bProcessed = src.bProcessed;
        }

        ~CDataNode()
        {
            //            pnRaw1 = null;
            //            pnRaw2 = null;
            pnDAQ = null;
            mut.Dispose();
        }   // ~CDataNode

    }   // public class CDataNode

    public class ImaqWrapper : IDisposable
    {
        bool disposed = false;
        public static UInt32 _IMG_BASE = 0x3FF60000;
        public static UInt32 IMG_ATTR_LAST_VALID_FRAME = _IMG_BASE + 0x00BA;
        public static UInt32 IMG_ATTR_ROI_WIDTH = _IMG_BASE + 0x01A6;
        public static UInt32 IMG_ATTR_ROI_HEIGHT = _IMG_BASE + 0x01A7;
        public static UInt32 IMG_ATTR_BITSPERPIXEL = _IMG_BASE + 0x0066;
        public static UInt32 IMG_OVERWRITE_GET_NEWEST = 3;
        
        [DllImport("imaq")]
        public static extern Int32 imgInterfaceOpen(char[] interfaceName, ref UInt32 pifid); // (const char* interfaceName, INTERFACE_ID* pifid)

        [DllImport("imaq")]
        public static extern Int32 imgSessionOpen(UInt32 ifid, ref UInt32 psid); // (INTERFACE_ID ifid, SESSION_ID* psid)

        [DllImport("imaq")]
        public static extern Int32 imgClose(UInt32 void_id, UInt32 freeResources); // (uInt32 void_id, uInt32 freeResources)

        //        [DllImport("imaq")]
        //        public static extern Int32 imgInterfaceReset(UInt32 ifid); // (INTERFACE_ID ifid)

        //        [DllImport("imaq")]
        //        public static extern Int32 imgCreateBufList(UInt32 numElements, ref UInt32 bid); // ((UInt32 numElements, BUFLIST_ID* bid)

        //        [DllImport("imaq")]
        //        public static extern Int32 imgDisposeBufList(UInt32 bid, UInt32 freeResources); // (BUFLIST_ID bid, uInt32 freeResources)

        //        [DllImport("imaq")]
        //        public static extern Int32 imgCreateBuffer(UInt32 sid, UInt32 where, UInt32 bufferSize, void** bufPtrAddr); // (SESSION_ID sid, UInt32 where, UInt32 bufferSize, void** bufPtrAddr)

        //        [DllImport("imaq")]
        //        public static extern Int32 imgDisposeBuffer(void* buffPtrAddr); // (void* buffPtrAddr)

        [DllImport("imaq")]
        public static extern Int32 imgGrab(UInt32 sid, ref IntPtr bufPtr, UInt32 waitForNext);  // (SESSION_ID sid, void** bufAddr, uInt32 waitForNext)

        [DllImport("imaq")]
        public static extern Int32 imgGrabSetup(UInt32 sid, UInt32 startNow); // (SESSION_ID sid, uint32 startNow)

        [DllImport("imaq")]
        public static extern Int32 imgSessionStartAcquisition(UInt32 sid); // (SESSION_ID sid)

        [DllImport("imaq")]
        public static extern Int32 imgSessionStopAcquisition(UInt32 sid); // (SESSION_ID sid)

        [DllImport("imaq")]
        public static extern Int32 imgSetAttribute2(UInt32 void_id, UInt32 attr, UInt32 value); // (uInt32 void_id, uInt32 attr, uInt32 value)

        [DllImport("imaq")]
        public static extern Int32 imgRingSetup(UInt32 sid, UInt32 numberOfBuffers, IntPtr[] bufferList, UInt32 skipCount, UInt32 startNow); // (SESSION_ID sid, uInt32 numberOfBuffers, void* bufferList[],uInt32 skipCount, uInt32 startNow)

        [DllImport("imaq")]
        public static extern Int32 imgSessionExamineBuffer2(UInt32 sid, UInt32 whichBuffer, ref UInt32 bufferNumber, ref IntPtr bufferAddr); // (SESSION_ID sid, uInt32 whichBuffer, void* bufferNumber, void** bufferAddr)

        [DllImport("imaq")]
        public static extern Int32 imgSessionReleaseBuffer(UInt32 sid); // (SESSION_ID sid)

        [DllImport("imaq")]
        public static extern Int32 imgSessionCopyBuffer(UInt32 sid, UInt32 bufferIndex, Int16[] buffer, UInt32 waitForNext); // (SESSION_ID sid, uInt32 bufferIndex, void* buffer, uInt32 waitForNext)

        [DllImport("imaq")]
        public static extern Int32 imgGetAttribute(UInt32 void_id, UInt32 attr, ref UInt32 value); // (uInt32 void_id, uInt32 attr, void* value);

        [DllImport("imaq")]
        public static extern Int32 imgSessionCopyBufferByNumber(UInt32 sid, UInt32 bufNum, Int16[] buffer, UInt32 attr, UInt32[] value1, UInt32[] value2); // (SESSION_ID sid, uInt32 bufNumber, void* userBuffer, IMG_OVERWRITE_MODE overwriteMode, uInt32* copiedNumber, uInt32* copiedIndex);

        //        public Int16[] grabImageData()
        //        {
        //            IntPtr p = new IntPtr();
        //            int rval = imgGrab(sessionPointer, ref p, waitForNext);
        //            manageError(rval);
        //            System.Runtime.InteropServices.Marshal.Copy(p, imageBuffer, 0, imageBuffer.Length);
        //            return imageBuffer;
        //        }

        public void Dispose()
        {
            if (!disposed)
            {
                Dispose(true);
                GC.SuppressFinalize(this);
                disposed = true;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                //free managed ressources
            }
            // free other ressources
        }

        ~ImaqWrapper()
        {
            Dispose(false);
        }
    }

}
