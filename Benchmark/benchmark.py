import subprocess
import socket
import struct
import time
import sys
import os
import ctypes
import threading
import numpy as np
import cv2
import win32gui
from PIL import ImageGrab
from pywinauto import Application
from pywinauto import WindowSpecification

# Benchmark attributes
exe_path = r"../Builds/SynchronisationSchemes.exe"
number_of_objects = 50
warmup_runs = 10
runs = 10
port1 = 9989
port2 = 9990

def main():
    # Change working directory to current file
    script_directory = os.path.dirname(os.path.abspath(__file__)) 
    os.chdir(script_directory)

    # Load processes with correct port
    process1 = subprocess.Popen([exe_path, str(port1)])
    process2 = subprocess.Popen([exe_path, str(port2)])
    time.sleep(5)

    # Place both processes on main desktop side by side
    screen_width, available_height = get_screen_size()
    app1 = Application().connect(process=process1.pid)
    window1 = app1.top_window()
    window1.set_focus()
    window1.move_window(x=0, y=0, width=screen_width // 2, height=available_height)
    app2 = Application().connect(process=process2.pid)
    window2 = app2.top_window()
    window2.set_focus()
    window2.move_window(x=screen_width // 2, y=0, width=screen_width // 2, height=available_height)

    # Establish tcp socket for communicating with benchmark controller
    process1_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    process1_socket.connect(('localhost', port1))
    process2_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    process2_socket.connect(('localhost', port2))
    time.sleep(2)

    # Benchmark
    benchmark(process1_socket, window1, process2_socket, window2, 'AuthoritativeSimulation', number_of_objects)
    benchmark(process1_socket, window1, process2_socket, window2, 'ClientServerSimulation', number_of_objects)

    # Cleanup
    process1_socket.close()
    process2_socket.close()
    process1.terminate()
    process2.terminate()

# TCP Flags
LoadScene = b'\x00'
UnloadScene = b'\x01'
StartHost = b'\x02'
StartClient = b'\x03'
StopHost = b'\x04'
StopClient = b'\x05'
GetBenchmark = b'\x06'
DirectionalInput = b'\x07'
SetObjectNumber = b'\x08'

def benchmark(process1: socket, window1: WindowSpecification, process2: socket, window2: WindowSpecification, scene: str, number_objects: int):
    scene_bytes = scene.encode('utf-8')
    process1.sendall(LoadScene + struct.pack('B', len(scene_bytes)) + scene_bytes)
    process2.sendall(LoadScene + struct.pack('B', len(scene_bytes)) + scene_bytes)
    time.sleep(1) # wait for scene to be loaded

    process1.sendall(SetObjectNumber + struct.pack('B', 4) + struct.pack('I', number_objects))
    process2.sendall(SetObjectNumber + struct.pack('B', 4) + struct.pack('I', number_objects))
    process1.sendall(StartHost + struct.pack('B', 0))
    process2.sendall(StartClient + struct.pack('B', 0))
    time.sleep(1) # wait for network to be started

    # Create the visual comparison thread
    cancellation_event = threading.Event()
    cancellation_event.clear()
    benchmark_thread = threading.Thread(target=capture_and_compare_videos, args=(window1, window2, cancellation_event, 10))
    benchmark_thread.start()

    # Client inputs for the benchmark
    directional_input(process2, 1.0, 0, 10)
    directional_input(process2, 0.8, 0.5, 3)
    directional_input(process2, 0, 0.5, 2)
    directional_input(process2, -0.3, -0.6, 9)

    cancellation_event.set()
    benchmark_thread.join()

    # Retrieve the results via the socket
    process2.sendall(GetBenchmark + struct.pack('B', 0))
    data_format = '<iQQ'
    data_size = struct.calcsize(data_format)
    data = b""
    while len(data) < data_size: # wait until all data is streamed
        packet = process2.recv(data_size - len(data))
        if not packet:
            break
        data += packet
    benchmark_duration, incoming_bytes, outgoing_bytes = struct.unpack(data_format, data)

    print(f"Result: {benchmark_duration} {incoming_bytes} {outgoing_bytes}")

    process1.sendall(UnloadScene + struct.pack('B', 0))
    process2.sendall(UnloadScene + struct.pack('B', 0))
    time.sleep(5)

def get_screen_size() -> tuple[int, int]:
    user32 = ctypes.windll.user32
    screen_width = user32.GetSystemMetrics(0)
    screen_height = user32.GetSystemMetrics(1)

    # Get the taskbar window handle
    taskbar_hwnd = user32.FindWindowW("Shell_TrayWnd", None)
    rect = ctypes.wintypes.RECT()
    user32.GetWindowRect(taskbar_hwnd, ctypes.byref(rect))
    
    # Calculate the taskbar height
    taskbar_height = rect.bottom - rect.top

    # Calculate available height for the windows
    available_height = screen_height - taskbar_height

    return screen_width, available_height

def get_window_size(window: WindowSpecification):
    # Get the client area rectangle of the window
    client_rect = win32gui.GetClientRect(window.handle)
    width = client_rect[2] - client_rect[0]
    height = client_rect[3] - client_rect[1]
    return width, height

def capture_window_frame(window: WindowSpecification) -> cv2.typing.MatLike:
    hwnd = window.handle
    # Get the client area rectangle
    client_rect = win32gui.GetClientRect(hwnd)

    # Convert client area coordinates to screen coordinates
    client_to_screen = win32gui.ClientToScreen(hwnd, (client_rect[0], client_rect[1]))
    left = client_to_screen[0]
    top = client_to_screen[1]
    right = left + client_rect[2]
    bottom = top + client_rect[3]

    # Capture the screenshot of the client area as a frame (RGB image)
    screenshot = ImageGrab.grab(bbox=(left, top, right, bottom))

    # Convert the screenshot to a numpy array (frame) for OpenCV
    frame = np.array(screenshot)
    frame = cv2.cvtColor(frame, cv2.COLOR_RGB2BGR)  # Convert from RGB to BGR for OpenCV
    return frame

def compute_frame_difference(frame1: cv2.typing.MatLike, frame2: cv2.typing.MatLike):
    # Convert both frames to grayscale
    gray1 = cv2.cvtColor(frame1, cv2.COLOR_BGR2GRAY)
    gray2 = cv2.cvtColor(frame2, cv2.COLOR_BGR2GRAY)
    
    # Compute the absolute difference between the two frames
    difference = cv2.absdiff(gray1, gray2)
    
    # Compute the percentage of non-zero pixels in the difference image
    non_zero_count = np.count_nonzero(difference)
    total_pixels = difference.size
    percent_difference = (non_zero_count / total_pixels) * 100
    
    return percent_difference

def capture_and_compare_videos(window1: WindowSpecification, window2: WindowSpecification, cancellation_event: threading.Event, fps=10):
    # Initialize video writers for both windows
    fourcc = cv2.VideoWriter_fourcc(*'XVID')
    out1 = cv2.VideoWriter("output1.avi", fourcc, fps, get_window_size(window1))
    out2 = cv2.VideoWriter("output2.avi", fourcc, fps, get_window_size(window2))

    total_difference = 0.0
    frame_count = 0
    while not cancellation_event.is_set():
        # Capture frames
        frame1 = capture_window_frame(window1)
        frame2 = capture_window_frame(window2)

        # Write frames to video files
        out1.write(frame1)
        out2.write(frame2)

        # Compare frames
        difference = compute_frame_difference(frame1, frame2)
        # print(f"Difference: {difference}%")
        
        total_difference += difference
        frame_count += 1
        time.sleep(1 / fps)

    if frame_count > 0:
        average_difference = total_difference / frame_count
    else:
        average_difference = 0.0

    # Release the video writer objects
    out1.release()
    out2.release()
    
    print(average_difference)

def directional_input(socket: socket, up: float, right: float, duration: float):
    socket.sendall(DirectionalInput + struct.pack('B', 8) + struct.pack('f', up) + struct.pack('f', right))
    time.sleep(duration)
    socket.sendall(DirectionalInput + struct.pack('B', 8) + struct.pack('f', 0) + struct.pack('f', 0))

if __name__ == "__main__":
    sys.exit(main())