using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ChakraHosting
{
    public class ChakraHost : IDisposable
    {
        private static JavaScriptSourceContext _currentSourceContext = JavaScriptSourceContext.FromIntPtr(IntPtr.Zero);
        private static JavaScriptRuntime _runtime;

        private static readonly Queue<JavaScriptValue> TaskQueue =
            new Queue<JavaScriptValue>();

        private bool _isPromiseLooping = false;

        private readonly CancellationTokenSource _shutdownCts = new CancellationTokenSource();
        private JavaScriptContext _context;

        public ChakraHost()
        {
            Init();
        }

        public JavaScriptValue GlobalObject { get; private set; }

        public void Dispose()
        {
            _shutdownCts.Cancel();
            _context.Release();
            _runtime.Dispose();
        }

        public void Init()
        {
            Native.ThrowIfError(Native.JsCreateRuntime(JavaScriptRuntimeAttributes.EnableExperimentalFeatures, null, out _runtime));
            Native.ThrowIfError(Native.JsCreateContext(_runtime, out _context));
            EnterContext();
            Native.ThrowIfError(Native.JsSetPromiseContinuationCallback(PromiseContinuationCallback, IntPtr.Zero));
            //Native.ThrowIfError(Native.JsProjectWinRTNamespace("Windows"));
            Native.ThrowIfError(Native.JsGetGlobalObject(out var global));
            GlobalObject = global;
            //Native.ThrowIfError(Native.JsStartDebugging());
            LeaveContext();
        }

        public void EnterContext()
        {
            Native.ThrowIfError(Native.JsSetCurrentContext(_context));
        }

        public void LeaveContext()
        {
            Native.ThrowIfError(Native.JsSetCurrentContext(JavaScriptContext.Invalid));
        }

        //public void WithContext(Action action)
        //{
        //    EnterContext();
        //    action.Invoke();
        //    LeaveContext();
        //}

        //public T WithContext<T>(Func<T> action)
        //{
        //    EnterContext();
        //    var result = action.Invoke();
        //    LeaveContext();
        //    return result;
        //}

        public JavaScriptValue RunScript(string script)
        {
            Native.ThrowIfError(Native.JsRunScript(script, _currentSourceContext++, "", out var result));
            return result;
        }

        private void PromiseContinuationCallback(JavaScriptValue task, IntPtr callbackState)
        {
            TaskQueue.Enqueue(task);
            task.AddRef();
            StartPromiseTaskLoop();
        }

        private void StartPromiseTaskLoop()
        {
            if (_isPromiseLooping)
            {
                return;
            }

            _isPromiseLooping = true;
            while (TaskQueue.Count != 0)
            {
                try
                {
                    var task = TaskQueue.Dequeue();
                    task.CallFunction(GlobalObject);
                    task.Release();
                }
                catch (OperationCanceledException e)
                {
                    return;
                }
            }

            _isPromiseLooping = false;
        }
    }
}