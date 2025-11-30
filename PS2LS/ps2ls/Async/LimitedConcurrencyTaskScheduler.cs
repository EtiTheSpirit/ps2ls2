using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace ps2ls.Async {
	/// <summary>
	/// Provides a task scheduler that enforces a maximum concurrency level while
	/// running on top of the <see cref="ThreadPool"/>, just like the default
	/// <see cref="TaskScheduler"/> does.
	/// - Xan 2025
	/// </summary>
	/// <remarks>
	/// Based on external source, but this specific variation is from my game and has been "downgraded" to support this version of .NET
	/// It comes from my game (-Xan). It basically allows the spam creation of tasks without completely overwhelming the system.
	/// </remarks>
	public sealed class LimitedConcurrencyTaskScheduler : TaskScheduler {

		[ThreadStatic]
		private static bool _busy;

		private readonly LinkedList<Task> _queue = new LinkedList<Task>();
		private readonly Mutex _queueLock = new Mutex();
		private readonly int _maxConcurrentTasks;
		private int _taskCount = 0;


		/// <inheritdoc/>
		public sealed override int MaximumConcurrencyLevel => _maxConcurrentTasks;

		/// <summary>
		/// Initializes an instance of the LimitedConcurrencyLevelTaskScheduler class with a maximum
		/// level of parallelism determined by the processor count of the current machine.
		/// </summary>
		public LimitedConcurrencyTaskScheduler() {
			_maxConcurrentTasks = System.Environment.ProcessorCount;
		}

		/// <summary>
		/// Initializes an instance of the LimitedConcurrencyLevelTaskScheduler class with the
		/// specified degree of parallelism, or <c>0</c> to use the processor count.
		/// </summary>
		/// <param name="maxDegreeOfParallelism">The amount of tasks that can be running at one time. Smaller values prevent overwhelming the computer, but skip out on its full potential as well. <c>0</c> will use the CPU's processor count.</param>
		public LimitedConcurrencyTaskScheduler(int maxDegreeOfParallelism) {
			//ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism, 0);
			if (maxDegreeOfParallelism < 0) throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));
			if (maxDegreeOfParallelism == 0) {
				_maxConcurrentTasks = System.Environment.ProcessorCount;
			} else {
				_maxConcurrentTasks = maxDegreeOfParallelism;
			}
		}

		/// <inheritdoc/>
		protected sealed override void QueueTask(Task task) {
			lock (_queueLock) {
				_queue.AddLast(task);
				if (_taskCount < _maxConcurrentTasks) {
					_taskCount++;
					AddPendingWork();
					// So for future Xan, what this is doing is basically starting up to [_maxConcurrentTasks] simultaneous
					// loops (see AddPendingWork) which each enumerate over the one task queue. Because these loops run in
					// their own thread, this basically means that the queue will be used across [_maxConcurrentTasks] threads,
					// thus enforcing the concurrency limit.
				}
			}
		}

		/// <summary>
		/// Updates the user work items on the thread pool.
		/// </summary>
		/// <remarks>This must be called when the task lock is held!</remarks>
		private void AddPendingWork() {
			ThreadPool.UnsafeQueueUserWorkItem(_anonWorkQueueMethod, this);
		}

		static void _anonWorkQueueMethod(object thisObject) {
			LimitedConcurrencyTaskScheduler @this = (LimitedConcurrencyTaskScheduler)thisObject;
			_busy = true;
			try {
				while (true) {
					Task item;
					lock (@this._queueLock) {
						LinkedListNode<Task> taskNode = @this._queue.First;
						if (taskNode == null) {
							@this._taskCount--;
							break;
						}
						@this._queue.RemoveFirst();
						item = taskNode.Value;
					}
					@this.TryExecuteTask(item);
				}
			} finally {
				_busy = false;
			}
		}


		/// <inheritdoc/>
		protected sealed override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) {
			if (!_busy) return false;
			if (taskWasPreviouslyQueued) TryDequeue(task);
			return TryExecuteTask(task);
		}


		/// <inheritdoc/>
		protected sealed override bool TryDequeue(Task task) {
			lock (_queueLock) {
				return _queue.Remove(task);
			}
		}

		/// <inheritdoc/>
		protected sealed override IEnumerable<Task> GetScheduledTasks() {
			if (_queueLock.WaitOne(0)) {
				Task[] tasks = _queue.ToArray();
				_queueLock.ReleaseMutex();
				return tasks;
			} else {
				throw new NotSupportedException("The tasks array is currently locked.");
			}
		}
	}
}
