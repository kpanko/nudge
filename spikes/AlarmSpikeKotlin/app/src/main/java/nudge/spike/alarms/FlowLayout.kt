package nudge.spike.alarms

import android.content.Context
import android.view.View
import android.view.ViewGroup

/**
 * Lays its children out left to right and wraps to a new row when one
 * is full, like the MAUI spike's wrapping FlexLayout. The platform
 * has no such layout without AndroidX.
 */
class FlowLayout(context: Context, private val gapPx: Int) :
    ViewGroup(context) {

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val maxWidth = MeasureSpec.getSize(widthMeasureSpec) -
            paddingLeft - paddingRight
        val childSpec = MeasureSpec.makeMeasureSpec(
            maxWidth,
            MeasureSpec.AT_MOST,
        )
        var x = 0
        var y = 0
        var rowHeight = 0
        var widest = 0
        for (child in visibleChildren()) {
            child.measure(childSpec, MeasureSpec.UNSPECIFIED)
            if (x > 0 && x + child.measuredWidth > maxWidth) {
                x = 0
                y += rowHeight + gapPx
                rowHeight = 0
            }
            x += child.measuredWidth + gapPx
            widest = maxOf(widest, x - gapPx)
            rowHeight = maxOf(rowHeight, child.measuredHeight)
        }

        setMeasuredDimension(
            resolveSize(
                widest + paddingLeft + paddingRight,
                widthMeasureSpec,
            ),
            resolveSize(
                y + rowHeight + paddingTop + paddingBottom,
                heightMeasureSpec,
            ),
        )
    }

    override fun onLayout(
        changed: Boolean,
        left: Int,
        top: Int,
        right: Int,
        bottom: Int,
    ) {
        val maxWidth = right - left - paddingLeft - paddingRight
        var x = 0
        var y = 0
        var rowHeight = 0
        for (child in visibleChildren()) {
            if (x > 0 && x + child.measuredWidth > maxWidth) {
                x = 0
                y += rowHeight + gapPx
                rowHeight = 0
            }
            child.layout(
                paddingLeft + x,
                paddingTop + y,
                paddingLeft + x + child.measuredWidth,
                paddingTop + y + child.measuredHeight,
            )
            x += child.measuredWidth + gapPx
            rowHeight = maxOf(rowHeight, child.measuredHeight)
        }
    }

    private fun visibleChildren(): List<View> =
        (0 until childCount)
            .map { getChildAt(it) }
            .filter { it.visibility != View.GONE }
}
