﻿Imports System.AddIn
Imports OpenSourceAutomation
Imports System.Timers
Imports System.IO.Ports
Imports System.Threading.Thread
'Imports System.Text.RegularExpressions

<AddIn("DSC Alarm", Version:="0.1.4")>
Public Class DSCAlarm
    Implements IOpenSourceAutomationAddInv2
    Private OSAEApi As New OSAE("DSC Alarm")
    Private AddInName As String
    'Private ComputerName As String
    Private COMPort As String
    Private BaudRate, UpdateTime As Integer
    Private ControllerPort As SerialPort
    Private UpdateTimer As Timer
    Private Message As String
    Dim ReceivedMessage As String
    Dim ReceiveTime As DateTime
    Dim SendString As String
    Dim PartitionStatus, PartitionStatusLast As String
    Dim ZoneArray(17), ZoneArrayLast(17) As String
    Dim ZoneInt As Integer

    Enum Command
        StatusRequest = 1
        PartitionArmControlAway = 30
        PartitionArmControlStay = 31
        PartitionArmControl = 33
        PartitionDisarmControl = 40

        CodeSend = 200

        CommandAcknowledge = 500
        CommandError = 501
        SystemError = 502
        RingDetected = 560

        ZoneAlarm = 601
        ZoneAlarmRestore = 602
        ZoneTamper = 603
        ZoneTamperRestore = 604
        ZoneFault = 605
        ZoneFaultRestore = 606
        ZoneOpen = 609
        ZoneRestored = 610
        DuressAlarm = 620
        FireKeyAlarm = 621
        FireKeyRestored = 622
        AuxiliaryKeyAlarm = 623
        AuxiliaryKeyRestored = 624
        PanicKeyAlarm = 625
        PanicKeyRestored = 626
        AuxiliaryInputAlarm = 632
        AuxiliaryInputAlarmRestored = 632

        PartitionReady = 650
        PartitionNotReady = 651
        PartitionArmed = 652
        PartitionReadyToForceArm = 653
        PartitionInAlarm = 654
        PartitionDisarmed = 655
        ExitDelayInProgress = 656
        EntryDelayInProgress = 657
        KeypadLockout = 658
        KeypadBlanking = 659
        CommandOutputInProgress = 660
        InvalidAccessCode = 670
        FunctionNotAvailable = 671
        FailedToArm = 672
        PartitionBusy = 673

        UserClosing = 700
        SpecialClosing = 701
        PartialClosing = 702
        UserOpening = 750
        SpecialOpening = 751

        TroubleLEDOn = 840
        TroubleLEDOff = 841

        KeybusFault = 896
        KeybusFaultRestore = 897

        CodeRequired = 900
        LCDUpdate = 901
        LEDStatus = 903
        SoftwareVersion = 908
    End Enum


    Public Sub RunInterface(ByVal pluginName As String) Implements IOpenSourceAutomationAddInv2.RunInterface
        Dim ZoneString As String
        Try
            OSAEApi.AddToLog("Initializing plugin: " & pluginName, True)
            AddInName = pluginName

            PartitionStatus = OSAEApi.GetObjectPropertyValue(AddInName, "PartitionStatus").Value
            PartitionStatusLast = PartitionStatus
            For ZoneNumber = 1 To 16
                ZoneString = ZoneNumber.ToString("D2")
                ZoneArray(ZoneNumber) = OSAEApi.GetObjectPropertyValue(AddInName, "Zone" & ZoneString).Value
                ZoneArrayLast(ZoneNumber) = ZoneArray(ZoneNumber)
            Next

            COMPort = "COM" + OSAEApi.GetObjectPropertyValue(pluginName, "Port").Value.ToString
            BaudRate = CInt(OSAEApi.GetObjectPropertyValue(pluginName, "Baud").Value.ToString)
            UpdateTime = CInt(OSAEApi.GetObjectPropertyValue(pluginName, "UpdateTime").Value.ToString)
            ControllerPort = New SerialPort(COMPort)
            OSAEApi.AddToLog("Port is set to " & COMPort & " and baud to " & BaudRate.ToString, True)
            ControllerPort.BaudRate = BaudRate
            ControllerPort.Parity = Parity.None
            ControllerPort.DataBits = 8
            ControllerPort.StopBits = 1
            ControllerPort.NewLine = vbCr & vbLf
            ControllerPort.ReadTimeout = 1

            ControllerPort.Open()

            AddHandler ControllerPort.DataReceived, New SerialDataReceivedEventHandler(AddressOf UpdateReceived)

            SendString = Int(Command.StatusRequest).ToString("D3")
            SendCommand(SendString)

            'Testing purposes only
            'ParseMessage("65211FF") 'Partition Armed
            'ParseMessage("656100") 'Partition Exit delay
            'ParseMessage("61000229") 'Zone 2 Closed
            'ParseMessage("60900228") 'Zone 2 Open

            UpdateTimer = New Timers.Timer(UpdateTime * 1000)
            AddHandler UpdateTimer.Elapsed, New ElapsedEventHandler(AddressOf TimerHandler)
            UpdateTimer.Enabled = True

        Catch ex As Exception
            OSAEApi.AddToLog("Error setting up plugin: " & ex.Message, True)
        End Try
    End Sub

    Public Sub ProcessCommand(method As OSAEMethod) Implements IOpenSourceAutomationAddInv2.ProcessCommand
        Try

        Catch ex As Exception
            OSAEApi.AddToLog("Error Processing Command " & ex.Message, True)
        End Try

    End Sub

    Public Sub Shutdown() Implements OpenSourceAutomation.IOpenSourceAutomationAddInv2.Shutdown
        Try
            OSAEApi.AddToLog("Shutting down plugin", True)
            If ControllerPort.IsOpen Then
                ControllerPort.Close()
            End If
            OSAEApi.AddToLog("Finished shutting down plugin", True)

        Catch ex As Exception
            OSAEApi.AddToLog("Error shutting down: " & ex.Message, True)
        End Try
    End Sub

    Protected Sub UpdateReceived(ByVal sender As Object, ByVal e As SerialDataReceivedEventArgs)
        Try
            OSAEApi.AddToLog("Running serial port event handler", False)
            ProcessReceived()
        Catch ex As Exception
            OSAEApi.AddToLog("Error in UpdateReceived " & ex.Message, True)
        End Try
    End Sub

    Protected Sub TimerHandler(ByVal sender As Object, ByVal e As ElapsedEventArgs)
        Try
            OSAEApi.AddToLog("Timer Update", False)
            SendString = Int(Command.StatusRequest).ToString("D3")
            SendCommand(SendString)
        Catch ex As Exception
            OSAEApi.AddToLog("Error in TimerHandler " & ex.Message, True)
        End Try
    End Sub

    Protected Sub SendCommand(message As String)
        Try
            message &= CalcChecksum(message) & vbCrLf
            ControllerPort.Write(message)
            OSAEApi.AddToLog("Sending message:" & message.TrimEnd & "| Message Lenght= " & message.Length.ToString, False)
        Catch ex As Exception
            OSAEApi.AddToLog("Error Sending Command " & ex.Message, True)
        End Try
    End Sub

    Protected Sub ProcessReceived()
        Dim Message As String
        Dim MyIndex As Integer
        Try
            Message = ControllerPort.ReadExisting()
            OSAEApi.AddToLog("Received: " & Message.TrimEnd, False)

            If Message.Length > 0 Then
                ReceivedMessage += Message
                While True
                    MyIndex = ReceivedMessage.IndexOf(vbCr & vbLf)
                    If MyIndex > -1 Then
                        ReceiveTime = Now()
                        ParseMessage(Left(ReceivedMessage, MyIndex))
                        ReceivedMessage = Mid(ReceivedMessage, MyIndex + 3)
                    Else
                        Exit While
                    End If
                End While
            End If

        Catch ex As Exception
            OSAEApi.AddToLog("Error receiving on com port:" & ex.Message, True)
        End Try
    End Sub

    Protected Sub ParseMessage(ByVal message As String)
        Dim RecCommand, LEDNum, LEDStatusNum As Integer
        Dim LEDStatus, LED, Zone As String
        Dim SoftwareVersion As String

        Try
            OSAEApi.AddToLog("Processing message:" & message, False)
            RecCommand = CInt(message.Substring(0, 3))

            If Not Command.IsDefined(GetType(Command), RecCommand) Then
                OSAEApi.AddToLog("Unrecognized Command:" & message, True)
            Else
                Select Case RecCommand
                    Case Command.CommandAcknowledge
                        OSAEApi.AddToLog("Command Acknowleged by Alarm", False)

                        'Partion Status
                    Case Command.PartitionReady
                        PartitionStatus = "Ready"
                        SetPartitionStatus(PartitionStatus)
                    Case Command.PartitionNotReady
                        PartitionStatus = "NotReady"
                        SetPartitionStatus(PartitionStatus)
                    Case Command.PartitionInAlarm
                        PartitionStatus = "InAlarm"
                        SetPartitionStatus(PartitionStatus)
                    Case Command.PartitionArmed
                        PartitionStatus = "Armed"
                        SetPartitionStatus(PartitionStatus)
                    Case Command.ExitDelayInProgress
                        PartitionStatus = "ExitDelayInProgress"
                        SetPartitionStatus(PartitionStatus)
                    Case Command.EntryDelayInProgress
                        PartitionStatus = "EntryDelayInProgress"
                        SetPartitionStatus(PartitionStatus)
                    Case Command.KeypadLockout
                        PartitionStatus = "KeypadLockout"
                        SetPartitionStatus(PartitionStatus)
                    Case Command.KeypadBlanking
                        PartitionStatus = "KeypadBlanking"
                        SetPartitionStatus(PartitionStatus)
                    Case Command.CommandOutputInProgress
                        PartitionStatus = "CommandOutputInProgress"
                        SetPartitionStatus(PartitionStatus)
                    Case Command.InvalidAccessCode
                        PartitionStatus = "InvalidAccessCode"
                        SetPartitionStatus(PartitionStatus)
                    Case Command.FunctionNotAvailable
                        PartitionStatus = "FunctionNotAvailable"
                        SetPartitionStatus(PartitionStatus)
                    Case Command.FailedToArm
                        PartitionStatus = "FailedToArm"
                        SetPartitionStatus(PartitionStatus)
                    Case Command.PartitionBusy
                        PartitionStatus = "Busy"
                        SetPartitionStatus(PartitionStatus)
                    Case Command.CodeRequired
                        PartitionStatus = "CodeRequired"
                        SetPartitionStatus(PartitionStatus)

                        'Trouble LED Commands
                    Case Command.TroubleLEDOn
                        OSAEApi.ObjectPropertySet(AddInName, "TroubleLED", "On")
                    Case Command.TroubleLEDOff
                        OSAEApi.ObjectPropertySet(AddInName, "TroubleLED", "Off")

                        'Keybus
                    Case Command.KeybusFault
                        OSAEApi.ObjectPropertySet(AddInName, "Keybus", "Fault")
                    Case Command.KeybusFaultRestore
                        OSAEApi.ObjectPropertySet(AddInName, "Keybus", "Restore")

                        'LCDUpdate
                    Case Command.LCDUpdate
                        'Do nothing

                        'LED Status
                    Case Command.LEDStatus
                        LEDNum = CInt(message.Substring(3, 1))
                        Select Case LEDNum
                            Case 1
                                LED = "Ready"
                            Case 2
                                LED = "Armed"
                            Case 3
                                LED = "Memory"
                            Case 4
                                LED = "Bypass"
                            Case 5
                                LED = "Trouble"
                            Case 6
                                LED = "Program"
                            Case 7
                                LED = "Fire"
                            Case 8
                                LED = "Backlight"
                            Case 9
                                LED = "AC"
                        End Select

                        LEDStatusNum = CInt(message.Substring(4, 1))
                        Select Case LEDStatusNum
                            Case 0
                                LEDStatus = "Off"
                            Case 1
                                LEDStatus = "On"
                            Case 2
                                LEDStatus = "Flashing"
                        End Select

                        OSAEApi.ObjectPropertySet(AddInName, "LED" & LED, LEDStatus)

                    Case Command.ZoneOpen, Command.ZoneRestored
                        Zone = message.Substring(4, 2)
                        ZoneInt = CInt(Zone)
                        If ZoneInt <= 16 And ZoneInt >= 1 Then
                            If RecCommand = Command.ZoneOpen Then
                                ZoneArray(ZoneInt) = "Open"
                                If ZoneArray(ZoneInt) <> ZoneArrayLast(ZoneInt) Then
                                    OSAEApi.EventLogAdd(AddInName, "Zone" & Zone & " Open")
                                    OSAEApi.ObjectPropertySet(AddInName, "Zone" & Zone, "Open")
                                End If
                            Else
                                ZoneArray(ZoneInt) = "Closed"
                                If ZoneArray(ZoneInt) <> ZoneArrayLast(ZoneInt) Then
                                    OSAEApi.EventLogAdd(AddInName, "Zone" & Zone & " Closed")
                                    OSAEApi.ObjectPropertySet(AddInName, "Zone" & Zone, "Closed")
                                End If
                            End If
                            ZoneArrayLast(Zone) = ZoneArray(Zone)
                        End If

                    Case Command.SoftwareVersion
                        SoftwareVersion = message.Substring(3, 6)
                        OSAEApi.ObjectPropertySet(AddInName, "SoftwareVersion", "SoftwareVersion")

                    Case Else
                        OSAEApi.AddToLog("Command not configured for message:" & message, True)
                End Select
                If PartitionStatus <> PartitionStatusLast Then
                    'Trigger Event
                    Select Case RecCommand
                        Case Command.PartitionArmed, Command.PartitionInAlarm, Command.ExitDelayInProgress, Command.EntryDelayInProgress
                            OSAEApi.AddToLog("Partition status`changed to " & PartitionStatus, True)
                            OSAEApi.EventLogAdd(AddInName, "Partition " & PartitionStatus)
                    End Select
                    PartitionStatusLast = PartitionStatus
                End If
            End If

        Catch ex As Exception
            OSAEApi.AddToLog("Error parsing message:" & ex.Message, True)
        End Try
    End Sub

    Sub SetPartitionStatus(PartitionStatus As String)
        OSAEApi.ObjectPropertySet(AddInName, "PartitionStatus", PartitionStatus)
    End Sub

    Function CalcChecksum(ByVal SendString As String) As String

        Try
            Dim CheckSum As Integer

            For Each Character In SendString.ToArray
                CheckSum += Convert.ToByte(Character)
            Next

            CheckSum = CheckSum And &HFF
            Return (CheckSum >> 4).ToString("X") & (CheckSum And &HF).ToString("X")

        Catch ex As Exception
            OSAEApi.AddToLog("Error in CalcChecksum " & ex.Message, True)
            Return "00"
        End Try
    End Function
End Class