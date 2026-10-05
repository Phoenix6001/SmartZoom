using System.Runtime.InteropServices;

using Accessibility;

using SmartZoom.Core.Input;
using SmartZoom.Interop.Accessibility;

namespace SmartZoom.Interop.Tests.Accessibility;

/// <summary>
/// The walk down a browser's accessibility tree, against a node that cannot answer. Real nodes fail in two ways:
/// they refuse (<see cref="COMException"/>), or they answer with a VARIANT the runtime cannot convert
/// (<see cref="InvalidCastException"/>, seen from Brave's <c>accHitTest</c>). Either way the walk stops at the
/// node it has, rather than the press failing.
/// </summary>
public sealed class MsaaContentHitTesterTests
{
    private static readonly ScreenPoint Point = new(400, 300);

    [Fact]
    public void A_node_whose_answer_cannot_be_converted_ends_the_descent_rather_than_the_press()
    {
        var node = new FailingNode(new InvalidCastException("Specified cast is not valid."));

        var (reached, child) = MsaaContentHitTester.Descend(node, Point);

        Assert.Same(node, reached);
        Assert.Equal(0, child);
    }

    [Fact]
    public void A_node_that_refuses_ends_the_descent_the_same_way()
    {
        // E_FAIL, what Chrome's accLocation answered while a page was still being built: the runtime's own
        // translation, so this is the exception a real refusal produces.
        var node = new FailingNode(Marshal.GetExceptionForHR(unchecked((int)0x80004005))!);

        var (reached, child) = MsaaContentHitTester.Descend(node, Point);

        Assert.Same(node, reached);
        Assert.Equal(0, child);
    }

    /// <summary>A node whose hit test fails the way a real one can; nothing else on it is used.</summary>
    private sealed class FailingNode(Exception failure) : IAccessible
    {
        public object accHitTest(int xLeft, int yTop) => throw failure;

        public object accParent => throw new NotSupportedException();

        public int accChildCount => throw new NotSupportedException();

        public object accFocus => throw new NotSupportedException();

        public object accSelection => throw new NotSupportedException();

        public object get_accChild(object varChild) => throw new NotSupportedException();

        public string get_accName(object varChild) => throw new NotSupportedException();

        public string get_accValue(object varChild) => throw new NotSupportedException();

        public string get_accDescription(object varChild) => throw new NotSupportedException();

        public object get_accRole(object varChild) => throw new NotSupportedException();

        public object get_accState(object varChild) => throw new NotSupportedException();

        public string get_accHelp(object varChild) => throw new NotSupportedException();

        public int get_accHelpTopic(out string pszHelpFile, object varChild) => throw new NotSupportedException();

        public string get_accKeyboardShortcut(object varChild) => throw new NotSupportedException();

        public string get_accDefaultAction(object varChild) => throw new NotSupportedException();

        public void accSelect(int flagsSelect, object varChild) => throw new NotSupportedException();

        public void accLocation(out int pxLeft, out int pyTop, out int pcxWidth, out int pcyHeight, object varChild) =>
            throw new NotSupportedException();

        public object accNavigate(int navDir, object varStart) => throw new NotSupportedException();

        public void accDoDefaultAction(object varChild) => throw new NotSupportedException();

        public void set_accName(object varChild, string pszName) => throw new NotSupportedException();

        public void set_accValue(object varChild, string pszValue) => throw new NotSupportedException();
    }
}
