using System;
using System.Collections.Generic;
using System.Reflection;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IModuleBus"/> 的进程内实现：一个主题一条信箱（FIFO）+ 一张弱引用订阅表。
/// <para>线程安全（单把锁）；派发在锁外完成，订阅者顺手再 <see cref="Post"/> 不会自锁。</para>
/// </summary>
public sealed class ModuleBus : IModuleBus
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<object?>> _mailboxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<WeakSubscription>> _subscribers = new(StringComparer.Ordinal);

    public void Post(string topic, object? payload)
    {
        if (string.IsNullOrWhiteSpace(topic)) return;

        WeakSubscription[] snapshot;
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(topic, out var live) || live.Count == 0)
            {
                Enqueue(topic, payload);
                return;
            }
            snapshot = live.ToArray();
        }

        var dead = new List<WeakSubscription>();
        var delivered = 0;
        foreach (var s in snapshot)
        {
            if (s.TryInvoke(payload)) delivered++;
            else dead.Add(s);
        }

        lock (_gate)
        {
            if (dead.Count > 0 && _subscribers.TryGetValue(topic, out var live))
            {
                foreach (var s in dead) live.Remove(s);
                if (live.Count == 0) _subscribers.Remove(topic);
            }
            // 订阅者全被回收了 ⇒ 这条消息没送出去，退回信箱等页面重建时取
            if (delivered == 0) Enqueue(topic, payload);
        }
    }

    public object? Take(string topic)
    {
        lock (_gate)
        {
            if (!_mailboxes.TryGetValue(topic, out var box) || box.Count == 0) return null;
            var one = box.Dequeue();
            if (box.Count == 0) _mailboxes.Remove(topic);
            return one;
        }
    }

    public IReadOnlyList<object?> TakeAll(string topic)
    {
        lock (_gate)
        {
            if (!_mailboxes.TryGetValue(topic, out var box) || box.Count == 0)
                return Array.Empty<object?>();

            var all = new List<object?>(box.Count);
            while (box.Count > 0) all.Add(box.Dequeue());
            _mailboxes.Remove(topic);
            return all;
        }
    }

    public int PendingCount(string topic)
    {
        lock (_gate)
        {
            return _mailboxes.TryGetValue(topic, out var box) ? box.Count : 0;
        }
    }

    public IDisposable Subscribe(string topic, object owner, Action<object?> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(handler);

        var entry = new WeakSubscription(owner, handler);
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(topic, out var live))
                _subscribers[topic] = live = new List<WeakSubscription>();
            live.Add(entry);
        }
        return new Unsubscriber(this, topic, entry);
    }

    private void Enqueue(string topic, object? payload)
    {
        if (!_mailboxes.TryGetValue(topic, out var box))
            _mailboxes[topic] = box = new Queue<object?>();
        box.Enqueue(payload);
    }

    private void Remove(string topic, WeakSubscription entry)
    {
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(topic, out var live)) return;
            live.Remove(entry);
            if (live.Count == 0) _subscribers.Remove(topic);
        }
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly ModuleBus _bus;
        private readonly string _topic;
        private readonly WeakSubscription _entry;
        private bool _done;

        public Unsubscriber(ModuleBus bus, string topic, WeakSubscription entry)
        {
            _bus = bus;
            _topic = topic;
            _entry = entry;
        }

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            _bus.Remove(_topic, _entry);
        }
    }

    /// <summary>弱引用订阅：只认 <c>owner</c> 的实例方法，因此不会把页面 VM 钉在内存里。</summary>
    private sealed class WeakSubscription
    {
        private readonly WeakReference<object> _owner;
        private readonly MethodInfo _method;

        public WeakSubscription(object owner, Delegate handler)
        {
            _owner = new WeakReference<object>(owner);
            _method = handler.Method;
        }

        public bool TryInvoke(object? payload)
        {
            if (!_owner.TryGetTarget(out var target)) return false;
            try { _method.Invoke(target, new object?[] { payload }); }
            catch { /* 单个订阅者抛异常不该把投递方一起拖垮 */ }
            return true;
        }
    }
}
