﻿using System;
using System.Collections.Generic;
using System.Linq;
using NWaves.Signals;
using NWaves.Transforms;
using NWaves.Utils;

namespace NWaves.Filters.Base
{
    /// <summary>
    /// Class responsible for OLA/OLS block convolution.
    /// It can be used as a filter (online filter as well).
    /// </summary>
    public class BlockConvolver : IFilter, IOnlineFilter
    {
        /// <summary>
        /// 
        /// </summary>
        protected Fft _fft;

        /// <summary>
        /// 
        /// </summary>
        protected int _fftSize;

        /// <summary>
        /// 
        /// </summary>
        protected float[] _kernel;

        //internal buffers

        private float[] _kernelSpectrumRe;
        private float[] _kernelSpectrumIm;
        private float[] _blockRe;
        private float[] _blockIm;
        private float[] _convRe;
        private float[] _convIm;
        private float[] _zeroblock;
        private float[] _lastSaved;

        /// <summary>
        /// 
        /// </summary>
        public int HopSize => _fftSize - _kernel.Length + 1;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="kernel"></param>
        /// <param name="fftSize"></param>
        public BlockConvolver(IEnumerable<float> kernel, int fftSize)
        {
            _fftSize = MathUtils.NextPowerOfTwo(fftSize);

            if (kernel.Count() > _fftSize)
            {
                throw new ArgumentException("Kernel length must not exceed the size of FFT!");
            }

            _fft = new Fft(_fftSize);

            _kernel = kernel.ToArray();
            _kernelSpectrumRe = _kernel.PadZeros(_fftSize);
            _kernelSpectrumIm = new float[_fftSize];
            _convRe = new float[_fftSize];
            _convIm = new float[_fftSize];
            _blockRe = new float[_fftSize];
            _blockIm = new float[_fftSize];
            _zeroblock = new float[_fftSize];

            _fft.Direct(_kernelSpectrumRe, _kernelSpectrumIm);

            Reset();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filter"></param>
        /// <param name="fftSize"></param>
        /// <returns></returns>
        public static BlockConvolver FromFilter(FirFilter filter, int fftSize)
        {
            fftSize = MathUtils.NextPowerOfTwo(fftSize);
            return new BlockConvolver(filter.ImpulseResponse().ToFloats(), fftSize);
        }

        /// <summary>
        /// Offline OLA/OLS filtering (essential the same as Operation.BlockConvolve() method)
        /// </summary>
        /// <param name="signal"></param>
        /// <param name="method"></param>
        /// <returns></returns>
        public DiscreteSignal ApplyTo(DiscreteSignal signal, FilteringMethod method = FilteringMethod.OverlapSave)
        {
            if (signal.Length < _fftSize)
            {
                return signal.Copy();
            }

            var blockConvolver = new BlockConvolver(_kernel, _fftSize);
            var filtered = new float[signal.Length + _kernel.Length - 1];

            var hopSize = HopSize;

            for (var i = 0; i < signal.Length; i += hopSize)
            {
                blockConvolver.Process(signal.Samples, filtered, _fftSize, i, i, method);
            }

            return new DiscreteSignal(signal.SamplingRate, filtered);
        }

        /// <summary>
        /// OLA/OLS online filtering
        /// </summary>
        /// <param name="input"></param>
        /// <param name="output"></param>
        /// <param name="count">Ignored</param>
        /// <param name="inputPos"></param>
        /// <param name="outputPos"></param>
        /// <param name="method"></param>
        public void Process(float[] input,
                            float[] output,
                            int count = 0,
                            int inputPos = 0,
                            int outputPos = 0,
                            FilteringMethod method = FilteringMethod.OverlapSave)
        {
            var M = _kernel.Length;

            var hopSize = HopSize;

            int n = inputPos, m = outputPos;

            _zeroblock.FastCopyTo(_blockRe, _fftSize);
            _zeroblock.FastCopyTo(_blockIm, _fftSize);

            /**
             *  ===================================== OVERLAP-ADD ========================================
             */ 
            if (method == FilteringMethod.OverlapAdd)
            {
                int k = Math.Min(hopSize, input.Length - n);

                input.FastCopyTo(_blockRe, k, n);

                _fft.Direct(_blockRe, _blockIm);
                for (var j = 0; j < _fftSize; j++)
                {
                    _convRe[j] = (_blockRe[j] * _kernelSpectrumRe[j] - _blockIm[j] * _kernelSpectrumIm[j]) / _fftSize;
                    _convIm[j] = (_blockRe[j] * _kernelSpectrumIm[j] + _blockIm[j] * _kernelSpectrumRe[j]) / _fftSize;
                }
                _fft.Inverse(_convRe, _convIm);

                for (var j = 0; j < M - 1; j++)
                {
                    _convRe[j] += _lastSaved[j];
                }

                _convRe.FastCopyTo(_lastSaved, M - 1, hopSize);
                _convRe.FastCopyTo(output, k, 0, m);
            }
            /**
             *  ===================================== OVERLAP-SAVE ========================================
             */
            else
            {
                int k = Math.Min(hopSize, input.Length - n);
                input.FastCopyTo(_blockRe, k, n, M - 1);

                _lastSaved.FastCopyTo(_blockRe, M - 1);

                _blockRe.FastCopyTo(_lastSaved, M - 1, k);

                _fft.Direct(_blockRe, _blockIm);
                for (var j = 0; j < _fftSize; j++)
                {
                    _convRe[j] = (_blockRe[j] * _kernelSpectrumRe[j] - _blockIm[j] * _kernelSpectrumIm[j]) / _fftSize;
                    _convIm[j] = (_blockRe[j] * _kernelSpectrumIm[j] + _blockIm[j] * _kernelSpectrumRe[j]) / _fftSize;
                }
                _fft.Inverse(_convRe, _convIm);

                _convRe.FastCopyTo(output, k, M - 1, m);
            }
        }

        /// <summary>
        /// Reset filter internals
        /// </summary>
        public void Reset()
        {
            _lastSaved = null;
            _lastSaved = new float[_kernel.Length - 1];
        }
    }
}
