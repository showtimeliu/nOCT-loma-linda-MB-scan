/**
 * This class provides a wrapper to invoke native C++ GPU functions from C#
 *
 * Tong Ling, Stanford University (tongling@stanford.edu) 10/28/2018
 */

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
using System.Security;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace nOCT
{
    [SuppressUnmanagedCodeSecurity]
    public static class GPUWrapper
    {
        [DllImport("NativeGPU.dll", CallingConvention = CallingConvention.Cdecl)]
        unsafe private static extern void init();

        [DllImport("NativeGPU.dll", CallingConvention = CallingConvention.Cdecl)]
        unsafe private static extern void apply_calib(double* res, short* pnIMAQ, double* pdReference, int rows, int cols, int nZPFactor, int nSkipLines);

        [DllImport("NativeGPU.dll", CallingConvention = CallingConvention.Cdecl)]
        unsafe private static extern void apply_disp_comp(double* pdIntensity, double* pdPhase, double* pcdCalibrated, double* pcdDispersionReal, double* pcdDispersionImag, int rows, int cols, int nSkipLines);

        public static void Initialization()
        {
            init();
        }

        unsafe public static void ApplyCalib(ComplexDouble[] pcdCalibrated, Int16[] pnFullImage, double[] pdReferenceRepMat, double[] pdCalibrationZPPoint, int[] pnCalibrationIndex, int nNumberLines, int nLineLength, int nZPFactor, int nSkipLines)
        {
            double[] pcdZPLine = new double[nZPFactor * nLineLength];
            double[] pdZPArray = new double[nZPFactor * nNumberLines * nLineLength / nSkipLines];

            fixed (short* _pnFullImage = pnFullImage)
            {
                fixed (double* _pdReferenceRepMat = pdReferenceRepMat)
                {
                    fixed (double* _pdZPArray = pdZPArray)
                    {
                        apply_calib(_pdZPArray, _pnFullImage, _pdReferenceRepMat, nLineLength, nNumberLines, nZPFactor, nSkipLines);

                        for (int nLine = 0; nLine < nNumberLines; nLine += nSkipLines)
                        {
                            for (int nPoint = 0; nPoint < nZPFactor * nLineLength; nPoint++)
                            {
                                pcdZPLine[nPoint] = pdZPArray[nLine / nSkipLines * nZPFactor * nLineLength + nPoint];                           // fetch zero-padding result
                            }
                            pcdCalibrated[nLine * nLineLength + 0].Real = pcdZPLine[0];
                            for (int nPoint = 1; nPoint < nLineLength - 1; nPoint++)                                                            // apply interpolation
                                pcdCalibrated[nLine * nLineLength + nPoint].Real = nZPFactor * ((nPoint * nZPFactor - pdCalibrationZPPoint[pnCalibrationIndex[nPoint]]) * (pcdZPLine[pnCalibrationIndex[nPoint] + 1] - pcdZPLine[pnCalibrationIndex[nPoint]]) / (pdCalibrationZPPoint[pnCalibrationIndex[nPoint] + 1] - pdCalibrationZPPoint[pnCalibrationIndex[nPoint]]) + pcdZPLine[pnCalibrationIndex[nPoint]]);
                            pcdCalibrated[nLine * nLineLength + nLineLength - 1].Real = pcdZPLine[nZPFactor * nLineLength - 1];
                        }
                      
                    }
                }
            }

        }

        unsafe public static void ApplyDispComp(double[,] pdIntensity, double[] pdPhase, double[] pcdCalibrated, double[] pcdDispersionReal, double[] pcdDispersionImag, int nNumberLines, int nLineLength, int nSkipLines)
        {
            double[] intensity = new double[nNumberLines * nLineLength / 2 / nSkipLines];
            double[] phase = new double[nNumberLines * nLineLength / 2 / nSkipLines];

            fixed (double* _pcdCalibrated = pcdCalibrated)
            {
                fixed (double* _pcdDispersionReal = pcdDispersionReal)
                {
                    fixed (double* _pcdDispersionImag = pcdDispersionImag)
                    {
                        fixed (double* _intensity = intensity)
                        {
                            fixed (double* _phase = phase)
                            {
                                apply_disp_comp(_intensity, _phase, _pcdCalibrated, _pcdDispersionReal, _pcdDispersionImag, nLineLength, nNumberLines, nSkipLines);

                                for (int nLine = 0; nLine < nNumberLines; nLine += nSkipLines)
                                {
                                    for (int nSkip = 0; nSkip < nSkipLines; nSkip++)
                                    {
                                        for (int nPoint = 0; nPoint < nLineLength / 2; nPoint++)
                                        {
                                            if (nLine + nSkip < nNumberLines)
                                            {
                                                pdIntensity[nLine + nSkip, nPoint] = intensity[nLine / nSkipLines * nLineLength / 2 + nPoint];
                                                pdPhase[(nLine + nSkip) * (nLineLength / 2) + nPoint] = phase[nLine / nSkipLines * nLineLength / 2 + nPoint];
                                            }
                                        }
                                    }
                                }

                            }
                        }
                    }
                }
            }
        }

        unsafe public static double CheckMarshalSum(double[] pdArray, int rows, int cols)
        {
            double sum = 0;

            fixed (double* _pdArray = pdArray)
            {
                for (int i = 0; i < rows * cols; i++)
                {
                    sum += pdArray[i] - _pdArray[i];
                }
            }

            return sum;
        }
    }
}
