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

LoadScene = b'\x00'
UnloadScene = b'\x01'
StartHost = b'\x02'
StartClient = b'\x03'
StopHost = b'\x04'
StopClient = b'\x05'
GetBenchmark = b'\x06'
DirectionalInput = b'\x07'
SetObjectNumber = b'\x08'

exe_path = r"../Builds/SynchronisationSchemes.exe"
warmup_runs = 10
runs = 10

def main():
    # change working directory to current file
    script_directory = os.path.dirname(os.path.abspath(__file__)) 
    os.chdir(script_directory)

    # load processes with correct port
    port1 = 9989
    port2 = 9990
    process1 = subprocess.Popen([exe_path, str(port1)])
    process2 = subprocess.Popen([exe_path, str(port2)])
    time.sleep(5)

    # place both processes on main desktop side by side
    screen_width, available_height = get_screen_size()
    app1 = Application().connect(process=process1.pid)
    window1 = app1.top_window()
    window1.set_focus()
    #window1.move_window(x=0, y=0, width=screen_width // 2, height=available_height)
    app2 = Application().connect(process=process2.pid)
    window2 = app2.top_window()
    window2.set_focus()
    #window2.move_window(x=screen_width // 2, y=0, width=screen_width // 2, height=available_height)

    # establish tcp socket for communicating with benchmark controller
    process1_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    process1_socket.connect(('localhost', port1))
    process2_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    process2_socket.connect(('localhost', port2))
    time.sleep(2)

    # benchmark
    number_of_objects = 10
    benchmark(process1_socket, window1, process2_socket, window2, 'AuthoritativeSimulation', number_of_objects)
    benchmark(process1_socket, window1, process2_socket, window2, 'ClientServerSimulation', number_of_objects)

    process1_socket.close()
    process2_socket.close()
    process1.terminate()
    process2.terminate()

def benchmark(process1: socket, window1: WindowSpecification, process2: socket, window2: WindowSpecification, scene: str, number_objects: int):
    scene_bytes = scene.encode('utf-8')
    process1.sendall(LoadScene + struct.pack('B', len(scene_bytes)) + scene_bytes)
    process2.sendall(LoadScene + struct.pack('B', len(scene_bytes)) + scene_bytes)
    time.sleep(1) # wait for scene to be loaded

    process1.sendall(SetObjectNumber + struct.pack('B', 4) + struct.pack('I', number_objects))
    process2.sendall(SetObjectNumber + struct.pack('B', 4) + struct.pack('I', number_objects))
    process1.sendall(StartHost + struct.pack('B', 0))
    process2.sendall(StartClient + struct.pack('B', 0))
    time.sleep(1)

    cancellation_event = threading.Event()
    cancellation_event.clear()
    benchmark_thread = threading.Thread(target=capture_and_compare_screenshots, args=(window1, window2, 1, cancellation_event))
    benchmark_thread.start()

    directional_input(process2, 1.0, 0, 2)
    directional_input(process2, 0.8, 0.5, 3)
    directional_input(process2, 0, 0.5, 2)
    directional_input(process2, -0.2, -0.5, 10)

    cancellation_event.set()
    benchmark_thread.join()

    #response = client_socket.recv(1024)
    #print(f"Received: {response.decode('utf-8')}")
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

def capture_window_screenshot(window: WindowSpecification, filename: str) -> str:
    window.set_focus()
    hwnd = window.handle
    # Get the client area rectangle
    client_rect = win32gui.GetClientRect(hwnd)
    
    # Convert client area coordinates to screen coordinates
    client_to_screen = win32gui.ClientToScreen(hwnd, (client_rect[0], client_rect[1]))
    left = client_to_screen[0]
    top = client_to_screen[1]
    right = left + client_rect[2]
    bottom = top + client_rect[3]
    
    # Capture the screenshot of the client area
    screenshot = ImageGrab.grab(bbox=(left, top, right, bottom))
    screenshot.save(filename)
    return filename

def compute_image_difference(img1_path: str, img2_path: str) -> float:
    # Load images
    img1 = cv2.imread(img1_path, cv2.IMREAD_GRAYSCALE)
    img2 = cv2.imread(img2_path, cv2.IMREAD_GRAYSCALE)
    
    # Compute absolute difference
    difference = cv2.absdiff(img1, img2)
    
    # Compute percentage difference
    non_zero_count = np.count_nonzero(difference)
    total_pixels = difference.size
    percent_difference = (non_zero_count / total_pixels) * 100
    
    return percent_difference

def capture_and_compare_screenshots(window1: WindowSpecification, window2: WindowSpecification, interval: int, cancellation_event: threading.Event):
    while not cancellation_event.is_set():
        timestamp = int(time.time())
        filename1 = f"screenshot1.png"
        filename2 = f"screenshot2.png"
        
        # Capture screenshots
        capture_window_screenshot(window1, filename1)
        capture_window_screenshot(window2, filename2)
        
        # Compute image difference
        difference = compute_image_difference(filename1, filename2)
        print(f"Timestamp: {timestamp}, Difference: {difference}%")
        
        time.sleep(interval)

def directional_input(socket: socket, up: float, right: float, duration: float):
    socket.sendall(DirectionalInput + struct.pack('B', 8) + struct.pack('f', up) + struct.pack('f', right))
    time.sleep(duration)
    socket.sendall(DirectionalInput + struct.pack('B', 8) + struct.pack('f', 0) + struct.pack('f', 0))

if __name__ == "__main__":
    sys.exit(main())