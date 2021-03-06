﻿/*
 * Copyright 2018 Creepsky
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *  http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using creepHashLib.Common;
using creepHashLib.Common.Logging;
using creepHashLib.Mining.Hardware;

namespace creepHashLib.Mining
{
    public abstract class MiningInstance
    {
        public string Algorithm { get; }
        public Miner Miner { get; }
        public IEnumerable<Hardware.Hardware> MiningHardware { get; }
        public Task Task { get; private set; }
        public string RewardCoin { get; }
        public string RewardAddress { get; }

        private readonly CancellationTokenSource _internCtx = new CancellationTokenSource();

        protected MiningInstance(Miner miner, IEnumerable<Hardware.Hardware> miningHardware, string algorithm,
            string rewardCoin, string rewardAddress)
        {
            Miner = miner ?? throw new ArgumentNullException(nameof(miner));
            MiningHardware = miningHardware ?? throw new ArgumentNullException(nameof(miningHardware));
            Algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
            RewardCoin = rewardCoin ?? throw new ArgumentNullException(nameof(rewardCoin));
            RewardAddress = rewardAddress ?? throw new ArgumentNullException(nameof(rewardAddress));

            if (!Miner.Algorithms.Contains(algorithm))
                throw new ArgumentOutOfRangeException(nameof(algorithm), $"{algorithm} is not minable with {Miner}");
        }

        public Task RunAsync(CancellationToken ctx)
        {
            Task = Task.Run(() => Run(_internCtx.Token, ctx), ctx);
            return Task;
        } 

        public abstract void Run(CancellationToken internCtx, CancellationToken externCtx);

        public void Cancel()
        {
            _internCtx.Cancel();
        }

        public static MiningInstance Create(Miner miner, IEnumerable<Hardware.Hardware> hardware, string algorithm,
            string rewardCoin, string rewardAddress, IPAddress ip, int port)
        {
            if (miner.Name == "ccminer")
                return new CcminerInstance(hardware, algorithm, rewardCoin, rewardAddress, ip, port);

            if (miner.Name == "ethminer")
                return new EthminerInstance(hardware, algorithm, rewardCoin, rewardAddress, ip, port);

            throw new ArgumentOutOfRangeException(nameof(miner), "The miner is not supported");
        }
    }

    public class CcminerInstance : MiningInstance
    {
        private readonly IPAddress _ip;
        private readonly int _port;

        public CcminerInstance(IEnumerable<Hardware.Hardware> miningHardware, string algorithm,
            string rewardCoin, string rewardAddress, IPAddress ip, int port)
            : base(Miner.FromString("ccminer"), miningHardware, algorithm, rewardCoin, rewardAddress)
        {
            _ip = ip;
            _port = port;
        }

        public override void Run(CancellationToken internCtx, CancellationToken externCtx)
        {
            try
            {
                var uri = $"stratum+tcp://{_ip}:{_port}";
                var parameter = $"-o {uri} -d {string.Join(",", MiningHardware.Select(i => i.Index))} -a {Algorithm} --no-color " +
                                $"-u {RewardCoin}:{RewardAddress}";
                Logger.Debug($"Starting {Miner.Path} {parameter}");

                Task.Run(() =>
                {
                    while (!internCtx.IsCancellationRequested)
                    {
                        if (externCtx.IsCancellationRequested)
                            Cancel();
                        else
                            externCtx.WaitHandle.WaitOne(1000);
                    }
                }, internCtx);

                foreach (var line in ProcessHelper.ReadLines(Miner.Path, parameter, internCtx))
                {
                    var rgx = Regex.Match(line, @"\[.*\]\s*(.*)");
                    Logger.Info(rgx.Groups[1].Value);
                }
            }
            catch (OperationCanceledException e)
            {
                if (e.CancellationToken.IsCancellationRequested)
                    e.CancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    public class EthminerInstance : MiningInstance
    {
        private readonly IPAddress _ip;
        private readonly int _port;

        public EthminerInstance(IEnumerable<Hardware.Hardware> miningHardware, string algorithm,
            string rewardCoin, string rewardAddress, IPAddress ip, int port)
            : base(Miner.FromString("ethminer"), miningHardware, algorithm, rewardCoin, rewardAddress)
        {
            _ip = ip;
            _port = port;
        }

        public override void Run(CancellationToken internCtx, CancellationToken externCtx)
        {
            try
            {
                var parameter = $"--farm-recheck 200 -S {_ip}:{_port} -u {RewardCoin}:{RewardAddress} ";

                if (MiningHardware.FirstOrDefault()?.Type == HardwareType.Cuda)
                {
                    parameter += $"-U --cuda-devices {string.Join(" ", MiningHardware.Select(i => i.Index))}";
                }
                else if (MiningHardware.FirstOrDefault()?.Type == HardwareType.OpenCl)
                {
                    parameter += $"-G --opencl-platform {string.Join(" ", MiningHardware.Select(i => i.PlatformIndex))} " +
                                 $"--opencl-devices {string.Join(" ", MiningHardware.Select(i => i.Index))}";
                }

                Logger.Debug($"Starting {Miner.Path} {parameter}");

                Task.Run(() =>
                {
                    while (!internCtx.IsCancellationRequested)
                    {
                        if (externCtx.IsCancellationRequested)
                            Cancel();
                        else
                            externCtx.WaitHandle.WaitOne(1000);
                    }
                }, internCtx);

                foreach (var line in ProcessHelper.ReadLines(Miner.Path, parameter, internCtx))
                    Logger.Info(line);
            }
            catch (OperationCanceledException e)
            {
                if (e.CancellationToken.IsCancellationRequested)
                    e.CancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}