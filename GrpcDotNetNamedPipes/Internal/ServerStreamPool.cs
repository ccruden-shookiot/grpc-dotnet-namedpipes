/*
 * Copyright 2020 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace GrpcDotNetNamedPipes.Internal
{
    internal class ServerStreamPool : IDisposable
    {
        private const int PoolSize = 4;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly string _pipeName;
        private readonly NamedPipeServerOptions _options;
        private readonly Func<NamedPipeServerStream, Task> _handleConnection;
        private bool _started;

        public ServerStreamPool(string pipeName, NamedPipeServerOptions options,
            Func<NamedPipeServerStream, Task> handleConnection)
        {
            _pipeName = pipeName;
            _options = options;
            _handleConnection = handleConnection;
        }

        private NamedPipeServerStream CreatePipeServer()
        {
            var pipeOptions = PipeOptions.Asynchronous;
#if NETCOREAPP || NETSTANDARD
#if !NETSTANDARD2_0
            if (_options.CurrentUserOnly)
            {
                pipeOptions |= PipeOptions.CurrentUserOnly;
            }
#endif

            return new NamedPipeServerStream(_pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Message,
                pipeOptions);
#endif
#if NETFRAMEWORK
            return new NamedPipeServerStream(_pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Message,
                pipeOptions,
                0,
                0,
                _options.PipeSecurity);
#endif
        }

        public void Start()
        {
            if (_started)
            {
                return;
            }

            for (int i = 0; i < PoolSize; i++)
            {
                StartListenThread();
            }

            _started = true;
        }

        private void StartListenThread()
        {
            var thread = new Thread(ListenForConnection);
            thread.Start();
        }

        private void ListenForConnection()
        {
            try
            {
                while (true)
                {
                    var pipeServer = CreatePipeServer();
                    pipeServer.WaitForConnectionAsync(_cts.Token).Wait();
                    Task.Run(() =>
                    {
                        try
                        {
                            _handleConnection(pipeServer).Wait();
                            pipeServer.Disconnect();
                        }
                        catch (Exception)
                        {
                            // TODO: Log
                        }
                        finally
                        {
                            pipeServer.Dispose();
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // TODO: Log
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
        }
    }
}