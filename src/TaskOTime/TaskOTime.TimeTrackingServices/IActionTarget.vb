Namespace ActiveDevelop.TimeTrackingServices
    Public Interface IActionTarget(Of ActionTargetIDType As {Structure, IComparable(Of ActionTargetIDType)})
        Inherits IComparable(Of IActionTarget(Of ActionTargetIDType))

        Property Id As ActionTargetIDType
    End Interface
End Namespace
