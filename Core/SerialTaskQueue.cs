﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IPBan
{
    /// <summary>
    /// A group of serial task queues
    /// </summary>
    public class SerialTaskQueue : IDisposable
    {
        /// <summary>
        /// Serial task queue instance
        /// </summary>
        private class SerialTaskQueueGroup : IDisposable
        {
            private readonly BlockingCollection<Func<Task>> taskQueue = new BlockingCollection<Func<Task>>();
            private readonly Task taskQueueRunner;
            private readonly CancellationTokenSource taskQueueRunnerCancel = new CancellationTokenSource();
            private readonly AutoResetEvent taskEmptyEvent = new AutoResetEvent(false);

            /// <summary>
            /// Cancel token - can pass this to tasks that are added to the task queue to allow them to cancel gracefully
            /// </summary>
            public CancellationToken CancelToken { get; private set; }

            /// <summary>
            /// Constructor
            /// </summary>
            public SerialTaskQueueGroup()
            {
                CancelToken = taskQueueRunnerCancel.Token;
                taskQueueRunner = StartQueue();
            }

            /// <summary>
            /// Dispose of the task queue
            /// </summary>
            public void Dispose()
            {
                try
                {
                    taskQueueRunnerCancel.Cancel();
                    Clear();
                }
                catch
                {
                }
            }

            /// <summary>
            /// Add an action
            /// </summary>
            /// <param name="action"></param>
            /// <returns>True if added, false if the queue is or has been disposed</returns>
            public bool Add(Func<Task> action)
            {
                if (!taskQueueRunnerCancel.IsCancellationRequested)
                {
                    taskQueue.Add(action);
                    return true;
                }
                return false;
            }

            /// <summary>
            /// Wait for the queue to empty
            /// </summary>
            /// <param name="timeout">Timeout</param>
            /// <returns>True if success, false if timeout</returns>
            public bool Wait(TimeSpan timeout = default)
            {
                return taskEmptyEvent.WaitOne(timeout == default ? Timeout.InfiniteTimeSpan : timeout);
            }

            /// <summary>
            /// Clear the task queue
            /// </summary>
            public void Clear()
            {
                while (taskQueue.TryTake(out _)) { }
                taskEmptyEvent.Set();
            }

            private Task StartQueue()
            {
                return Task.Run(() =>
                {
                    try
                    {
                        while (!taskQueueRunnerCancel.IsCancellationRequested)
                        {
                            if (taskQueue.TryTake(out Func<Task> runner, -1, taskQueueRunnerCancel.Token))
                            {
                                try
                                {
                                    Task task = runner();
                                    task.Wait(-1, taskQueueRunnerCancel.Token);
                                    if (taskQueue.Count == 0)
                                    {
                                        taskEmptyEvent.Set();
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                    break;
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                    Dispose();
                });
            }
        }

        private readonly Dictionary<string, SerialTaskQueueGroup> taskQueues = new Dictionary<string, SerialTaskQueueGroup>(StringComparer.OrdinalIgnoreCase);

        private bool disposed;

        /// <summary>
        /// Dispose of the task queue
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            try
            {
                disposed = true;
                SerialTaskQueueGroup[] groups;
                lock (this)
                {
                    groups = taskQueues.Values.ToArray();
                    taskQueues.Clear();
                }
                foreach (SerialTaskQueueGroup group in groups)
                {
                    group.Dispose();
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Add an action
        /// </summary>
        /// <param name="action"></param>
        /// <param name="name">Queue name or empty string for default</param>
        /// <returns>True if added, false if the queue is or has been disposed</returns>
        public bool Add(Func<Task> action, string name = "")
        {
            if (disposed || name == null)
            {
                return false;
            }
            SerialTaskQueueGroup group;
            lock (this)
            {
                if (!taskQueues.TryGetValue(name, out group))
                {
                    taskQueues[name] = group = new SerialTaskQueueGroup();
                }
            }
            if (group.CancelToken.IsCancellationRequested)
            {
                return false;
            }
            group.Add(action);
            return true;
        }

        /// <summary>
        /// Wait for the queue to empty
        /// </summary>
        /// <param name="timeout">Timeout</param>
        /// <param name="name">Queue name, empty string for default or null to wait for all queues</param>
        /// <returns>True if success, false if timeout</returns>
        public bool Wait(TimeSpan timeout = default, string name = "")
        {
            SerialTaskQueueGroup[] groups;
            lock (this)
            {
                groups = taskQueues.Where(t => t.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(t => t.Value).ToArray();
            }
            if (groups.Length == 0)
            {
                return false;
            }
            foreach (SerialTaskQueueGroup group in groups)
            {
                if (!group.Wait(timeout))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Clear all pending operations on task queues
        /// </summary>
        /// <param name="name">The queue to clear, empty string for default or null to clear all queues</param>
        public void Clear(string name = null)
        {
            SerialTaskQueueGroup[] groups;

            lock (this)
            {
                if (name == null)
                {
                    groups = taskQueues.Values.ToArray();
                }
                else if (taskQueues.TryGetValue(name, out SerialTaskQueueGroup _group))
                {
                    groups = new SerialTaskQueueGroup[] { _group };
                }
                else
                {
                    return;
                }
            }
            foreach (SerialTaskQueueGroup group in groups)
            {
                group.Clear();
            }
        }

        /// <summary>
        /// Get a cancellation token for a queue
        /// </summary>
        /// <param name="name">Queue name, empty string for default</param>
        /// <returns>Cancellation token or default if queue not found</returns>
        public CancellationToken GetToken(string name = "")
        {
            name.ThrowIfNull(nameof(name));

            lock (this)
            {
                if (taskQueues.TryGetValue(name, out SerialTaskQueueGroup group))
                {
                    return group.CancelToken;
                }
            }
            return default;
        }
    }
}
